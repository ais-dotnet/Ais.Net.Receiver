// <copyright file="DeadLetterStore.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Storage;

/// <summary>
/// A local, on-disk queue of storage batches that could not be persisted. Each batch is written as a
/// single newline-separated <c>.nm4</c> file so it survives a process restart and can be replayed once
/// the storage backend recovers. Writes are atomic (staged to a temporary name, then renamed) so a
/// reader never observes a half-written file.
/// </summary>
public sealed class DeadLetterStore
{
    private const string Extension = ".nm4";
    private const string TempExtension = ".nm4.tmp";

    private readonly string directory;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadLetterStore"/> class.
    /// </summary>
    /// <param name="directory">The directory in which dead-letter files are stored.</param>
    /// <param name="timeProvider">The time provider used to name files.</param>
    public DeadLetterStore(string directory, TimeProvider timeProvider)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Writes a batch to a new dead-letter file, atomically (staged then renamed).
    /// </summary>
    /// <param name="batch">The batch of raw sentence bytes to persist locally.</param>
    /// <returns>The path of the file that was written.</returns>
    public async Task<string> WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> batch)
    {
        Directory.CreateDirectory(this.directory);

        // A sortable UTC timestamp prefix keeps the directory listing in chronological order, so
        // replay is roughly first-in-first-out; the GUID keeps concurrent writers from colliding.
        string name = $"deadletter-{this.timeProvider.GetUtcNow():yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}";
        string tempPath = Path.Combine(this.directory, name + TempExtension);
        string finalPath = Path.Combine(this.directory, name + Extension);

        await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write))
        {
            foreach (ReadOnlyMemory<byte> message in batch)
            {
                await stream.WriteAsync(message).ConfigureAwait(false);
                stream.WriteByte((byte)'\n');
            }
        }

        // Rename is atomic on the same volume, so the file only becomes visible as a replayable
        // ".nm4" once it is completely written.
        File.Move(tempPath, finalPath);
        return finalPath;
    }

    /// <summary>
    /// Lists the pending dead-letter files, oldest first. Partially written (<c>.nm4.tmp</c>) files are
    /// excluded.
    /// </summary>
    /// <returns>The pending file paths in chronological order.</returns>
    public IReadOnlyList<string> GetPendingFiles()
    {
        if (!Directory.Exists(this.directory))
        {
            return [];
        }

        // Filter by suffix explicitly rather than via a "*.nm4" search pattern: on Windows a
        // three-character extension pattern can also match longer extensions (so it would wrongly
        // include the ".nm4.tmp" staging files).
        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(this.directory))
        {
            if (file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }

        // The timestamp-prefixed names sort chronologically under ordinal comparison.
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Reads a dead-letter file back into the batch of messages it holds.
    /// </summary>
    /// <param name="path">The dead-letter file path.</param>
    /// <returns>The messages contained in the file.</returns>
    public static async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ReadBatchAsync(string path)
    {
        byte[] content = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        List<ReadOnlyMemory<byte>> messages = [];
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == (byte)'\n')
            {
                if (i > start)
                {
                    messages.Add(content.AsMemory(start, i - start));
                }

                start = i + 1;
            }
        }

        // Tolerate a final line with no trailing newline.
        if (start < content.Length)
        {
            messages.Add(content.AsMemory(start));
        }

        return messages;
    }

    /// <summary>
    /// Removes a dead-letter file once its batch has been replayed.
    /// </summary>
    /// <param name="path">The dead-letter file path.</param>
    public static void Remove(string path) => File.Delete(path);
}
