import { bindValue } from "cs2/api";

export const group = "BrandTheBuilding";

export const isToolActive$ = bindValue<boolean>(group, "isToolActive", false);
export const isEditing$ = bindValue<boolean>(group, "isEditing", false);
export const hoverText$ = bindValue<string>(group, "hoverText", "");
export const statusText$ = bindValue<string>(group, "statusText", "");
export const buildingName$ = bindValue<string>(group, "buildingName", "");
export const assetName$ = bindValue<string>(group, "assetName", "");
export const assetIndex$ = bindValue<number>(group, "assetIndex", 0);
export const assetCount$ = bindValue<number>(group, "assetCount", 0);
export const offset$ = bindValue<number>(group, "offset", 0);
export const canPlace$ = bindValue<boolean>(group, "canPlace", false);
