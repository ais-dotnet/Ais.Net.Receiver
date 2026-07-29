const DAYS = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT'];
const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];

export function formatTimestamp(baseEpoch, offsetSeconds) {
  const date = new Date((baseEpoch + offsetSeconds) * 1000);
  const day = DAYS[date.getUTCDay()];
  const dd = String(date.getUTCDate()).padStart(2, '0');
  const mon = MONTHS[date.getUTCMonth()];
  const year = date.getUTCFullYear();
  const hh = String(date.getUTCHours()).padStart(2, '0');
  const mm = String(date.getUTCMinutes()).padStart(2, '0');
  return `${day}, ${dd} ${mon} ${year} ${hh}:${mm} GMT`;
}
