export function getTooltip({object}) {
  if (!object) return null;

  const pos = object._currentPos;
  const speed = pos ? pos.speed.toFixed(1) : '--';
  const course = pos ? pos.course.toFixed(1) : '--';

  return {
    html: `<div style="line-height:1.5">
      <strong>${object.name}</strong><br/>
      <span style="color:#aaa">${object.shipTypeCategory || object.shipType || 'Unknown'}</span><br/>
      MMSI: ${object.mmsi}<br/>
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
