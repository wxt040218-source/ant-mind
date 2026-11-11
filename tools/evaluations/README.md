# Agent MCP reasoning evaluation

`agent-v017.xml` contains ten independent read-only questions for v0.17 and the default content13 definitions. Use an already running default-content host and expose its official MCP tools to the model under evaluation. Do not reset or mutate a user's world to run these questions. Catalog-based answers require the default definitions; do not silently grade a modified mod catalog against them.

Each answer is an exact string. Questions combine tool contracts, progression, item definitions, recipes or building/worker constraints. They complement runtime regression tests and do not replace them. The answers were verified through the live MCP catalog and tool schemas on 2026-11-10; **no model-scored evaluation run has been performed**. Temporary evidence is in `artifacts/agent-events-evaluation-*` and is intentionally not committed.
