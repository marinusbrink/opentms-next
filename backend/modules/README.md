# OpenTMS modules

One ABP module per domain from the domain map in `/CLAUDE.md` (owned by the PO), plus the
horizontal **Platform** layer. Each module has five projects:

| Project | Contents | Who may reference it |
|---|---|---|
| `*.Domain.Shared` | consts, enums, localization, **published events (ETOs)** | everyone (published surface) |
| `*.Application.Contracts` | DTOs, app-service interfaces, permission names | everyone (published surface) |
| `*.Domain` | entities, domain services | own module + app composition root |
| `*.Application` | app services | own module + app composition root |
| `*.EntityFrameworkCore` | DbContext (own schema), repositories | own module + app composition root |

Hard rules (enforced by `test/OpenTms.ArchitectureTests`):

1. A domain module never references another domain module's `Domain`, `Application` or
   `EntityFrameworkCore` project — cross-module communication is local event bus or the
   published surface (`Domain.Shared`, `Application.Contracts`) only.
2. **Platform may be referenced by all modules; Platform references no domain module.**
3. Each module's DbContext maps only its own schema; no cross-schema joins.
