# Ticket 07 — Flow-Safety Rules

Insertion validates recursive scope legality. PhaseGoto is accepted only when its target is a valid exit of the source Phase and does not cross the destination Phase boundary. SessionGoto is accepted only in a direct SessionDecision option sequence; it creates a fresh projected output socket and copies no edges. RETURN remains legal wherever the destination scope permits it.

PromptChoice is limited to three options and retains the existing nested SessionGoto prohibition.
