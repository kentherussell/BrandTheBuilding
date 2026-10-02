import type { ModRegistrar } from "cs2/modding";
import { BrandTheBuildingOverlay, UniversalMenuButton } from "./ui";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", BrandTheBuildingOverlay);

  moduleRegistry.append("UniversalModMenu", UniversalMenuButton);
};

export default register;
