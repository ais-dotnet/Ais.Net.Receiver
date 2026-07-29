/**
 * Escapes text for interpolation into the tooltip's html property, which deck.gl assigns via
 * innerHTML. Vessel names arrive over the air from AIS transmitters (6-bit ASCII includes < > = /),
 * so they are untrusted input and must never reach the DOM unescaped.
 */
function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, c => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  })[c]);
}

export function getTooltip({object}) {
  if (!object) return null;

  const pos = object._currentPos;
  const speed = pos ? pos.speed.toFixed(1) : '--';
  const course = pos ? pos.course.toFixed(1) : '--';

  return {
    html: `<div style="line-height:1.5">
      <strong>${escapeHtml(object.name)}</strong><br/>
      <span style="color:#aaa">${escapeHtml(object.shipTypeCategory || object.shipType || 'Unknown')}</span><br/>
      MMSI: ${escapeHtml(object.mmsi)}<br/>
      Speed: ${speed} kn &middot; Course: ${course}&deg;
    </div>`,
    style: {
      backgroundColor: 'rgba(0, 0, 0, 0.85)',
      color: '#fff',
      fontSize: '13px',
      padding: '8px 12px',
      borderRadius: '4px',
      border: '1px solid rgba(255,255,255,0.1)',
      backdropFilter: 'blur(8px)'
    }
  };
}
