const assert = require("node:assert/strict");
const { test, beforeEach, afterEach } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const ts = require("typescript");
const { JSDOM } = require("jsdom");

const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "http://localhost" });
global.window = dom.window;
global.document = dom.window.document;
Object.defineProperty(global, "navigator", { value: dom.window.navigator, configurable: true });
global.HTMLElement = dom.window.HTMLElement;
global.IS_REACT_ACT_ENVIRONMENT = true;

const React = require("react");
const { render, screen, cleanup, fireEvent } = require("@testing-library/react");
const userEvent = require("@testing-library/user-event").default;
let values;
let calls;
let user;

// Only the game host and stylesheet imports are replaced. React owns state,
// effects, focus, and event bubbling just as it does in the overlay.
const api = {
  bindValue: (group, name, fallback) => ({ group, name, fallback }),
  useValue: binding => values[binding.name] ?? binding.fallback,
  trigger: (...args) => calls.push(args)
};

function loadSource(filename, dependencies) {
  const source = fs.readFileSync(path.join(__dirname, "../src", filename), "utf8");
  const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.React, esModuleInterop: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(compiled, {
    exports,
    require(name) {
      assert.ok(Object.hasOwn(dependencies, name), `Unexpected dependency: ${name}`);
      return dependencies[name];
    }
  }, { filename });
  return exports;
}

const bindings = loadSource("bindings.ts", { "cs2/api": api });
function HostButton({ selected, onSelect, tooltipLabel, variant, children, ...props }) {
  return React.createElement("button", { ...props, "aria-pressed": selected, onClick: onSelect }, children);
}
const { BrandTheBuildingOverlay, UniversalMenuButton } = loadSource("ui.tsx", {
  react: React,
  "cs2/api": api,
  "cs2/ui": { Button: HostButton },
  "./bindings": bindings,
  "./ui.module.scss": new Proxy({}, { get: (_, key) => key })
});
const overlay = () => React.createElement(BrandTheBuildingOverlay);
const offsetInput = () => screen.getByRole("textbox", { name: "Surface offset in meters" });
const command = (name, ...args) => ["BrandTheBuilding", name, ...args];
const sent = name => calls.filter(call => call[1] === name);

beforeEach(() => {
  values = {
    isToolActive: true, isEditing: true, hoverText: "", statusText: "",
    buildingName: "124 Long Boulevard", assetName: "Billboard Large",
    assetIndex: 1, assetCount: 2, offset: -0.10, canPlace: true
  };
  calls = [];
  user = userEvent.setup({ document });
});
afterEach(cleanup);

test("the inactive tool leaves no overlay or placement controls", () => {
  values.isToolActive = false;
  const view = render(overlay());
  assert.equal(view.container.innerHTML, "");
  assert.deepEqual(calls, []);
});

test("targeting shows the building prompt without exposing editing controls", () => {
  values.isEditing = false;
  values.hoverText = "No company occupies this building.";
  const view = render(overlay());
  assert.ok(screen.getByText(values.hoverText));
  assert.equal(screen.queryByRole("button", { name: "Place" }), null);
  assert.equal(screen.queryByRole("textbox"), null);

  values.isEditing = true;
  view.rerender(overlay());
  assert.equal(screen.queryByText(values.hoverText), null);
  assert.ok(screen.getByRole("heading", { name: values.buildingName }));
});

test("an unnamed building and asset still have usable labels", () => {
  values.buildingName = "";
  values.assetName = "";
  render(overlay());
  assert.ok(screen.getByRole("heading", { name: "Selected building" }));
  assert.ok(screen.getByText("Branding asset"));
});

test("typing an offset waits until blur, then sends the value once", async () => {
  render(overlay());
  assert.equal(offsetInput().value, "-0.10");
  await user.clear(offsetInput());
  await user.type(offsetInput(), "-2.75");
  assert.deepEqual(sent("setOffset"), []);
  await user.tab();
  assert.deepEqual(sent("setOffset"), [command("setOffset", -2.75)]);
});

test("Enter commits by moving focus out of the offset field", async () => {
  render(overlay());
  await user.clear(offsetInput());
  await user.type(offsetInput(), "0.50{Enter}");
  assert.notEqual(document.activeElement, offsetInput());
  assert.deepEqual(sent("setOffset"), [command("setOffset", 0.5)]);
});

test("a decimal comma and surrounding whitespace are accepted", async () => {
  render(overlay());
  await user.clear(offsetInput());
  await user.type(offsetInput(), " -1,25 ");
  await user.tab();
  assert.deepEqual(sent("setOffset"), [command("setOffset", -1.25)]);
});

for (const draft of ["", "   ", "sign", "1.2.3", "Infinity", "NaN", "1e999"]) {
  test(`invalid offset ${JSON.stringify(draft)} restores the last game value`, async () => {
    render(overlay());
    await user.clear(offsetInput());
    if (draft) await user.type(offsetInput(), draft);
    await user.tab();
    assert.deepEqual(sent("setOffset"), []);
    assert.equal(offsetInput().value, "-0.10");
  });
}

test("game updates refresh the offset unless the player is typing", async () => {
  const view = render(overlay());
  values.offset = 0.25;
  view.rerender(overlay());
  assert.equal(offsetInput().value, "0.25");

  await user.clear(offsetInput());
  await user.type(offsetInput(), "-1.5");
  values.offset = 0.75;
  view.rerender(overlay());
  assert.equal(offsetInput().value, "-1.5");
  await user.tab();
  assert.deepEqual(sent("setOffset"), [command("setOffset", -1.5)]);
  assert.equal(offsetInput().value, "0.75");
});

test("a new editing session does not inherit an unfinished offset", async () => {
  const view = render(overlay());
  await user.clear(offsetInput());
  await user.type(offsetInput(), "-8");
  values.isEditing = false;
  view.rerender(overlay());
  values.offset = -0.03;
  values.isEditing = true;
  view.rerender(overlay());
  assert.equal(offsetInput().value, "-0.03");
  assert.deepEqual(sent("setOffset"), []);
});

test("offset arrows send small steps, or ten steps with Shift", async () => {
  render(overlay());
  const decrease = screen.getByRole("button", { name: "Decrease surface offset" });
  const increase = screen.getByRole("button", { name: "Increase surface offset" });
  await user.click(decrease);
  await user.click(increase);
  await user.keyboard("{Shift>}");
  await user.click(decrease);
  await user.click(increase);
  await user.keyboard("{/Shift}");
  assert.deepEqual(sent("stepOffset"), [-1, 1, -10, 10].map(step => command("stepOffset", step)));
});

test("asset navigation is disabled with fewer than two choices", async () => {
  const view = render(overlay());
  const previous = () => screen.getByRole("button", { name: "Previous branding asset" });
  const next = () => screen.getByRole("button", { name: "Next branding asset" });
  for (const count of [0, 1]) {
    values.assetCount = count;
    view.rerender(overlay());
    assert.equal(previous().disabled, true);
    assert.equal(next().disabled, true);
    await user.click(previous());
    await user.click(next());
  }
  assert.deepEqual(sent("previousAsset"), []);
  assert.deepEqual(sent("nextAsset"), []);

  values.assetCount = 2;
  view.rerender(overlay());
  await user.click(previous());
  await user.click(next());
  assert.deepEqual(sent("previousAsset"), [command("previousAsset")]);
  assert.deepEqual(sent("nextAsset"), [command("nextAsset")]);
});

test("Place cannot send a request until surface validation allows it", async () => {
  values.canPlace = false;
  const view = render(overlay());
  const place = screen.getByRole("button", { name: "Place" });
  assert.equal(place.disabled, true);
  await user.click(place);
  assert.deepEqual(sent("place"), []);

  values.canPlace = true;
  view.rerender(overlay());
  assert.equal(place.disabled, false);
  calls.length = 0;
  await user.click(place);
  assert.deepEqual(calls.slice(-2), [command("setPointerOverUI", false), command("place")]);
});

test("Cancel releases the UI pointer before exiting, even when Place is disabled", async () => {
  values.canPlace = false;
  render(overlay());
  await user.click(screen.getByRole("button", { name: "Cancel" }));
  assert.deepEqual(calls.slice(-2), [command("setPointerOverUI", false), command("cancel")]);
});

test("the editing panel blocks mouse actions from reaching the game surface", async () => {
  const leaked = [];
  render(React.createElement("div", {
    onClick: () => leaked.push("click"),
    onMouseDown: () => leaked.push("mouseDown"),
    onPointerDown: () => leaked.push("pointerDown")
  }, overlay()));
  await user.click(screen.getByRole("button", { name: "Next branding asset" }));
  await user.click(offsetInput());
  await user.type(offsetInput(), "2");
  assert.deepEqual(leaked, []);
  assert.equal(document.activeElement, offsetInput());
});

test("action buttons leave a focused offset alone until it is explicitly committed", async () => {
  render(overlay());
  await user.clear(offsetInput());
  await user.type(offsetInput(), "-2");
  await user.click(screen.getByRole("button", { name: "Next branding asset" }));
  assert.equal(document.activeElement, offsetInput());
  assert.deepEqual(sent("setOffset"), []);
  assert.deepEqual(sent("nextAsset"), [command("nextAsset")]);
});

test("pointer entry and exit tell the tool when to suspend surface dragging", () => {
  const view = render(overlay());
  const panel = view.container.querySelector("section");
  fireEvent.pointerEnter(panel);
  fireEvent.pointerLeave(panel);
  assert.deepEqual(calls, [command("setPointerOverUI", true), command("setPointerOverUI", false)]);
});

test("the menu activates an idle tool and cancels an active one", async () => {
  values.isToolActive = false;
  const view = render(React.createElement(UniversalMenuButton));
  const button = screen.getByRole("button", { name: "Brand the Building" });
  assert.equal(button.getAttribute("aria-pressed"), "false");
  await user.click(button);
  assert.deepEqual(calls, [command("activate")]);

  values.isToolActive = true;
  view.rerender(React.createElement(UniversalMenuButton));
  assert.equal(button.getAttribute("aria-pressed"), "true");
  await user.click(button);
  assert.deepEqual(calls, [command("activate"), command("cancel")]);
});
