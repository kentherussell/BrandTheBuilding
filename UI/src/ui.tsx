import React from "react";
import { Button } from "cs2/ui";
import { trigger, useValue } from "cs2/api";
import {
  assetCount$,
  assetIndex$,
  assetName$,
  buildingName$,
  canPlace$,
  group,
  hoverText$,
  isEditing$,
  isToolActive$,
  offset$,
  statusText$
} from "./bindings";
import styles from "./ui.module.scss";

function invoke(name: string) {
  trigger(group, name);
}

function stop(event: React.SyntheticEvent) {
  event.stopPropagation();
}

function Chevron({ direction }: { direction: "left" | "right" }) {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d={direction === "left" ? "M15 4 7 12l8 8" : "m9 4 8 8-8 8"} />
    </svg>
  );
}

export function UniversalMenuButton() {
  const active = useValue(isToolActive$);
  return (
      <Button
        variant="floating"
        selected={active}
        className={styles.menuButton}
        aria-label="Brand the Building"
        tooltipLabel="Brand the Building"
        onSelect={() => invoke(active ? "cancel" : "activate")}
      >
        <span className={styles.icon} aria-hidden="true">
          <svg viewBox="0 0 24 24">
            <rect x="3" y="3.5" width="18" height="11" rx="1.5" />
            <path d="M12 14.5V21M8 21h8M6.5 7h11M6.5 10.5h7" />
          </svg>
        </span>
      </Button>
  );
}

export function BrandTheBuildingOverlay() {
  const active = useValue(isToolActive$);
  const editing = useValue(isEditing$);
  const hoverText = useValue(hoverText$);
  const statusText = useValue(statusText$);
  const buildingName = useValue(buildingName$);
  const assetName = useValue(assetName$);
  const assetIndex = useValue(assetIndex$);
  const assetCount = useValue(assetCount$);
  const offset = useValue(offset$);
  const canPlace = useValue(canPlace$);
  const [offsetDraft, setOffsetDraft] = React.useState(offset.toFixed(2));
  const [editingOffset, setEditingOffset] = React.useState(false);

  React.useEffect(() => {
    if (!editingOffset) {
      setOffsetDraft(offset.toFixed(2));
    }
  }, [offset, editingOffset]);

  React.useEffect(() => {
    if (!editing) {
      setEditingOffset(false);
      setOffsetDraft(offset.toFixed(2));
    }
  }, [editing, offset]);

  function commitOffset() {
    setEditingOffset(false);
    const trimmed = offsetDraft.trim().replace(",", ".");
    const parsed = trimmed === "" ? NaN : Number(trimmed);
    if (Number.isFinite(parsed)) {
      trigger(group, "setOffset", parsed);
    } else {
      setOffsetDraft(offset.toFixed(2));
    }
  }

  if (!active) {
    return null;
  }

  return (
    <>
      {!editing && (
        <>
          <section className={styles.targetingPanel}>
            <div className={styles.modeBadge}>
              <span className={styles.modePulse} />
              Targeting mode
            </div>
            <h2>Add Company Branding</h2>
            <p className={styles.targetInstruction}>Hover over a company building to choose a surface.</p>
            <div className={styles.keyHint}>
              <kbd>Esc</kbd>
              <span>Exit tool</span>
            </div>
          </section>
          {hoverText && (
            <div className={`${styles.hoverPrompt} ${hoverText.startsWith("Can't") || hoverText.startsWith("No ") ? styles.invalidPrompt : ""}`}>
              {hoverText}
            </div>
          )}
        </>
      )}

      {editing && (
        <section
          className={styles.panel}
          onPointerEnter={() => trigger(group, "setPointerOverUI", true)}
          onPointerLeave={() => trigger(group, "setPointerOverUI", false)}
          onMouseEnter={() => trigger(group, "setPointerOverUI", true)}
          onMouseLeave={() => trigger(group, "setPointerOverUI", false)}
          onPointerDown={(event) => event.stopPropagation()}
          onMouseDown={(event) => {
            event.stopPropagation();
            // Action buttons disappear on exit. Avoid focusing a DOM node that
            // is about to unmount; the offset textbox still receives focus.
            if ((event.target as Element).closest("button")) {
              event.preventDefault();
            }
          }}
          onClick={(event) => event.stopPropagation()}
        >
          <header>
            <div className={styles.modeBadge}>
              <span className={styles.modePulse} />
              Editing placement
            </div>
            <h2>{buildingName || "Selected building"}</h2>
            <p className={styles.subtitle}>Choose a sign, position it, then place it.</p>
          </header>

          <div className={styles.editInstruction}>
            <span>Click and drag on this building to move the sign.</span>
          </div>

          <div className={styles.assetPicker}>
            <button
              type="button"
              tabIndex={-1}
              aria-label="Previous branding asset"
              className={assetCount <= 1 ? styles.disabled : ""}
              disabled={assetCount <= 1}
              onClick={(event) => {
                stop(event);
                invoke("previousAsset");
              }}
            >
              <Chevron direction="left" />
            </button>

            <div className={styles.assetName}>
              <strong>{assetName || "Branding asset"}</strong>
              <span>{assetIndex} of {assetCount}</span>
            </div>

            <button
              type="button"
              tabIndex={-1}
              aria-label="Next branding asset"
              className={assetCount <= 1 ? styles.disabled : ""}
              disabled={assetCount <= 1}
              onClick={(event) => {
                stop(event);
                invoke("nextAsset");
              }}
            >
              <Chevron direction="right" />
            </button>
          </div>

          <div className={styles.offsetLabel}>Surface offset</div>
          <div className={styles.offsetPicker}>
            <button
              type="button"
              tabIndex={-1}
              aria-label="Decrease surface offset"
              onClick={(event) => {
                stop(event);
                trigger(group, "stepOffset", event.shiftKey ? -10 : -1);
              }}
            >
              <Chevron direction="left" />
            </button>
            <label className={styles.offsetValue}>
              <input
                type="text"
                inputMode="decimal"
                aria-label="Surface offset in meters"
                value={offsetDraft}
                onFocus={() => setEditingOffset(true)}
                onChange={(event) => setOffsetDraft(event.target.value)}
                onBlur={commitOffset}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.currentTarget.blur();
                  }
                }}
              />
              <span>m</span>
            </label>
            <button
              type="button"
              tabIndex={-1}
              aria-label="Increase surface offset"
              onClick={(event) => {
                stop(event);
                trigger(group, "stepOffset", event.shiftKey ? 10 : 1);
              }}
            >
              <Chevron direction="right" />
            </button>
          </div>

          <p className={styles.status}>{statusText}</p>

          <div className={styles.keyHint}>
            <kbd>Esc</kbd>
            <span>Cancel and exit without placing</span>
          </div>

          <div className={styles.actions}>
            <button
              type="button"
              tabIndex={-1}
              className={styles.cancel}
              onClick={(event) => {
                stop(event);
                trigger(group, "setPointerOverUI", false);
                invoke("cancel");
              }}
            >
              Cancel
            </button>
            <button
              type="button"
              tabIndex={-1}
              className={`${styles.place} ${!canPlace ? styles.disabled : ""}`}
              disabled={!canPlace}
              onClick={(event) => {
                stop(event);
                trigger(group, "setPointerOverUI", false);
                invoke("place");
              }}
            >
              Place
            </button>
          </div>
        </section>
      )}
    </>
  );
}
