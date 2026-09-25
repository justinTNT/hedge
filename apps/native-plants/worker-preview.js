import application from "./dist/server/Worker.js";
import { protectPreview } from "./preview-gate.mjs";
export default protectPreview(application);
