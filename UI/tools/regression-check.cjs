// Execute the actual UI component with lightweight host bindings. These checks
// cover event contracts, not Cohtml rendering or the game's C# lifecycle.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const ts = require("typescript");

const values = {
  isToolActive$: true, isEditing$: true, hoverText$: "", statusText$: "",
  buildingName$: "124 Long Boulevard", assetName$: "Billboard Large",
  assetIndex$: 1, assetCount$: 2, offset$: -0.10, canPlace$: true
};
const calls = [];
let state = [];
let cursor = 0;
const react = {
  createElement: (type, props, ...children) => ({ type, props: props || {}, children }),
  useState: initial => {
    const slot = cursor++;
    if (!(slot in state)) state[slot] = initial;
    return [state[slot], value => { state[slot] = value; }];
  },
  useEffect: () => {}
};
const bindings = { group: "BrandTheBuilding" };
for (const name of Object.keys(values)) bindings[name] = name;
const exportsObject = {};
const source = fs.readFileSync(path.join(__dirname, "../src/ui.tsx"), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.React, esModuleInterop: true }
}).outputText;
vm.runInNewContext(compiled, {
  exports: exportsObject,
  require(name) {
    if (name === "react") return react;
    if (name === "cs2/ui") return { Button: "NativeButton" };
    if (name === "cs2/api") return {
      useValue: binding => values[binding],
      trigger: (...args) => calls.push(args)
    };
    if (name === "./bindings") return bindings;
    if (name.endsWith(".scss")) return {};
    throw new Error(`Unexpected UI dependency: ${name}`);
  }
});
function render() { cursor = 0; return exportsObject.BrandTheBuildingOverlay(); }
function nodes(root) {
  if (!root || typeof root !== "object") return [];
  return [root, ...root.children.flatMap(nodes)];
}
function find(root, predicate) {
  const result = nodes(root).find(predicate);
  assert.ok(result, "Expected control was not rendered");
  return result;
}
const click = shiftKey => ({ shiftKey, stopPropagation() {} });
let tree = render();
for (const [label, direction] of [["Decrease surface offset", -1], ["Increase surface offset", 1]]) {
  const button = find(tree, node => node.props["aria-label"] === label);
  for (const shift of [false, true]) {
    calls.length = 0;
    button.props.onClick(click(shift));
    assert.deepEqual(calls, [["BrandTheBuilding", "stepOffset", direction * (shift ? 10 : 1)]]);
  }
}
assert.equal(find(tree, node => node.type === "h2").children[0], values.buildingName$);
let input = find(tree, node => node.type === "input");
assert.equal(input.props.value, "-0.10");
calls.length = 0;
input.props.onChange({ target: { value: "-2.75" } });
assert.equal(calls.length, 0, "Typing must not send an offset yet");
tree = render();
input = find(tree, node => node.type === "input");
input.props.onBlur();
assert.deepEqual(calls, [["BrandTheBuilding", "setOffset", -2.75]]);
let blurred = false;
input.props.onKeyDown({ key: "Enter", currentTarget: { blur() { blurred = true; } } });
assert.equal(blurred, true, "Enter must commit through blur");
values.canPlace$ = false;
assert.equal(find(render(), node => node.children.includes("Place")).props.disabled, true);
values.canPlace$ = true;
assert.equal(find(render(), node => node.children.includes("Place")).props.disabled, false);
const menu = exportsObject.UniversalMenuButton();
assert.equal(menu.type, "NativeButton");
assert.equal(menu.props.variant, "floating");
assert.equal(menu.props.tooltipLabel, "Brand the Building");
console.log("UI regression checks passed: normal/Shift steps, deferred offset, address heading, Place gate, native menu.");
