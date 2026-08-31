# Ticket 08 — Phase ActionNode Parity

Phase ActionNodes use the same ActionSequence editor and block insertion path as Cards. The editor passes the containing Phase id into validation, so phase-local flow checks work for direct nodes and recursive PromptChoice descendants. Graph reload applies pending selection to the inserted rows.
