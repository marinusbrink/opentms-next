# Financial module — internal submodules

Financial keeps a strict internal submodule structure (design §3.1): **Tariffs**, **Invoicing**,
**Purchasing** (carrier settlement) and **TransportUnits** (transport-unit balances). They are
folders/namespaces inside the Financial projects — `OpenTms.Financial.Tariffs.*` etc. — **not**
separate ABP modules yet, so that a later split is a deployment decision, not a rebuild.

Rules inside this module:

- Every new type goes into exactly one submodule folder/namespace; nothing at the module root
  except shared Financial plumbing.
- Submodules share the `financial` schema and the `FinancialDbContext`, but keep table name
  prefixes per submodule when entities arrive.
- Splitting a submodule into its own ABP module is a PO decision via the design gate.
