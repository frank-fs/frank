/**
 * Pi port of the [PERSON_NAME] Code Stop hook
 * `.claude/settings.json` -> hooks.Stop -> `bash hooks/check-new-package-deliverables.sh`.
 *
 * Runs the repo's existing script after the agent settles (equivalent to the
 * end-of-response Stop hook) and surfaces its stderr warnings in the TUI.
 * The script itself stays the single source of truth for the checks.
 */
import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";

export default function (pi: ExtensionAPI) {
	pi.on("agent_settled", async (_event, ctx) => {
		const result = await pi.exec("bash", ["hooks/check-new-package-deliverables.sh"]);
		const warnings = (result.stderr ?? "").trim();
		if (warnings.length > 0) {
			ctx.ui.notify(warnings, "warning");
		}
	});
}