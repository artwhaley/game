# Ticket 03 — Template Serialization

`ActionBlockSerializer` serializes every currently registered action type, including recursive PromptChoice options, configured values, blocking flags, and source-phase metadata. Deserialization rejects malformed JSON and unsupported format versions with user-facing validation errors.

Materialization assigns fresh action and nested-option ids through the caller-provided id factory. The source template remains unchanged.
