// Numerical model + source guards, not a substitute for C#/game execution.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const dot = (a, b) => a.reduce((s, v, i) => s + v * b[i], 0);
const add = (a, b) => a.map((v, i) => v + b[i]);
const scale = (a, s) => a.map(v => v * s);
const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
const unit = a => scale(a, 1 / Math.hypot(...a));
const near = (a, b) => assert(Math.abs(a-b) < 1e-8, `${a} != ${b}`);
function rotate(v, n, angle) {
  return add(add(scale(v, Math.cos(angle)), scale(cross(n, v), Math.sin(angle))),
    scale(n, dot(n, v) * (1-Math.cos(angle))));
}
for (const normal of [[0,1,0], unit([0.5,1,0.4])]) {
  const initial = unit(add([0,0,1], scale(normal, -normal[2])));
  for (const degrees of [-180,-90,-30,0,30,90,180,720]) {
    const forward = rotate(initial, normal, degrees*Math.PI/180);
    const right = unit(cross(normal, forward));
    near(dot(forward, normal), 0);
    near(dot(right, normal), 0);
    near(Math.hypot(...forward), 1);
    // Base-center anchoring with off-center bounds: rotation must not move anchor.
    const centerX=1.2, centerZ=-0.8, minY=-0.2, clearance=0.04;
    const origin = add(add(scale(right,-centerX),scale(normal,clearance-minY)),scale(forward,-centerZ));
    const base = add(origin,add(add(scale(right,centerX),scale(normal,minY)),scale(forward,centerZ)));
    base.forEach((v,i)=>near(v,normal[i]*clearance));
    // Roof probes remain on the plane while the footprint rotates.
    for (const x of [-4,4]) for (const z of [-1.5,1.5])
      near(dot(add(scale(right,x),scale(forward,z)),normal),0);
  }
}
const source = fs.readFileSync(path.join(root,'Systems/BrandPlacementToolSystem.cs'),'utf8');
assert(source.includes('!m_SupportPending && !m_IsRotating && m_RotationDelta == 0f'));
assert(source.includes('m_RoofYaw = m_PendingRoofYaw;'));
const reject = source.slice(source.indexOf('private void RejectPendingSurface()'),source.indexOf('private void DrawPreviewOutline()'));
assert(!reject.includes('m_RoofYaw ='));
assert(source.includes('quaternion.AxisAngle(normal, roofYaw ?? m_RoofYaw)'));
assert(source.includes('m_RotationCameraBarrier?.Dispose();'));
assert(source.includes('"Camera", "Rotate", "BrandTheBuildingRoofRotation"'));
assert(!/TracePreviewLifecycle|BeginPlacementDiagnostics|TracePlacementDiagnostics/.test(source));
assert(!fs.existsSync(path.join(root,'Systems/BrandPlacementDiagnostics.cs')));
console.log('PASS: flat/sloped roof rotation, base anchor, probe plane, validation/input source guards, diagnostic removal.');
