"""Read-only repository context -> SDD revision. Writes only below this SDD directory."""
from pathlib import Path
import json,re,textwrap
R=Path(__file__).resolve().parents[1];REPO=Path('/home/phucpinku/Desktop/Git/GrainLedger');OLD=R.parent/'archive/SDD_before_repository_context_2026-10-16';D=R/'diagrams'
md=(OLD/'GrainLedger_SDD.md').read_text();manifest=json.loads((OLD/'design_manifest.json').read_text())
def table(head,rows):
 return '| '+' | '.join(head)+' |\n| '+' | '.join(['---']*len(head))+' |\n'+''.join('| '+' | '.join(str(v).replace('\n','<br>') for v in x)+' |\n' for x in rows)+'\n'
backend=[
 ('GrainLedger.Api','Existing composition root: ASP.NET Core 10 Minimal API host, middleware/authentication, module registration, same-origin static web hosting and development OpenAPI/Scalar.'),
 ('GrainLedger.Modules.Identity','Existing module. Owns identity schema, users/organizations, permissions, cookie/JWT sessions, local/Google authentication and administration.'),
 ('Identity.Domain / Features / Authorization','Existing directories. Domain/User, Organization, LoginSession; Features/Authentication, Profile, Administration; persisted permission/scope resolution.'),
 ('Identity.Infrastructure / Common','Existing directories. EF Core IdentityDbContext, cookie/JWT/CSRF adapters, SMTP email sender, request/error helpers.'),
 ('GrainLedger.Modules.Catalogs','Existing module. Endpoint classes, catalog entities and CatalogDbContext in the module root; commodity/unit/substance/market/quality/variety and versioned MRL APIs.'),
 ('GrainLedger.Modules.Capture','Existing module. Endpoint classes, EF entities, CaptureDbContext, request/response records and capture schema. Keeps immutable procurement source expectations distinct from accepted physical stock.'),
 ('Capture.IProcurementLedgerPort','Existing in-process interface in ProcurementLedgerPort.cs; RegisterPendingAsync uses the caller connection/transaction. No EF entities cross the boundary.'),
 ('GrainLedger.Modules.Ledger','Existing module. LedgerEndpoints and LedgerManagementEndpoints, LedgerService, ProcurementLedgerPort/CommodityLedgerPort, protected reads/commands and evidence handling.'),
 ('Ledger.Sql','Existing SQL resource directory: Schema, Operations, Commands, Reads, PhysicalReceiving, CorrectionBinding and later migrations define protected state, postings and logical domain records.'),
 ('Module migrations and DbContexts','Existing module-owned persistence/migration history. Identity, Catalogs and Capture use EF domain persistence; LedgerDbContext supports migrations while runtime LedgerService uses Npgsql/protected SQL.'),
 ('Compliance capability (planned logical module)','Target design for lab/cohort/propagation/assessments; consumes Catalogs MRL references and actual accepted Ledger quantities/lineage. No corresponding project is present yet; final project/namespace split remains a design decision.'),
 ('Export capability (planned logical module)','Target order/plan/review/reservation/loading/CLOSE/dossier coordination in the same host, sharing the protected transaction boundary. No corresponding project is present yet.'),
 ('Trace/container-HOLD extensions (planned)','Extend Ledger lineage/HOLD/history to complete lab-to-container and container-to-plot workflows; current lot HOLD does not automatically cascade.')]
web=[
 ('apps/web/src/main.tsx; App.tsx','Existing React/TypeScript entry point, restored session, scope-aware navigation and page composition; built using Vite.'),
 ('src/api.ts; auth-errors.ts','Existing same-origin fetch wrapper: HttpOnly session cookies, X-CSRF-TOKEN on mutations, session-generation checks and ProblemDetails errors. No access token is stored in local storage.'),
 ('src/LocalAuth.tsx; Administration.tsx','Existing local-auth/account and role/organization-access screens; Google login is brokered by backend Identity.'),
 ('src/CatalogScreen.tsx','Existing catalog and versioned MRL screens.'),
 ('src/LedgerWorkspace.tsx; LedgerReceiving.tsx','Existing task workspace and source-registration / actual-arrival measurement / manager-review interface.'),
 ('src/LedgerOperations.tsx; LedgerSetup.tsx','Existing processing/draft/correction/transfer and configuration screens.'),
 ('src/LedgerStock.tsx; LedgerLotPicker.tsx','Existing stock, HOLD/notices/history views and scoped lot selection.'),
 ('src/ledger-ui.tsx; styles.css','Existing shared UI/numeric-input/pagination components and responsive styles. These are files in a flat src directory, not invented nested packages.'),
 ('Compliance/export screens (planned)','Future evidence, bounds, plan review, loading, CLOSE and dossier views; retain the current API/session/error conventions.'),
 ('Durable offline field capture (planned)','Future local pending storage/reconnection/reconciliation in the mobile-friendly web client. A server CaptureOperation record is present, but the complete offline client is not implemented.')]
field=[('apps/web mobile-friendly capture screens','Existing browser surface to extend; the repository apps/mobile package is a placeholder, not a running native client.'),('Local capture store (planned)','Persist original pending payload and capture metadata; use stable device/operation identity, dependency ordering and owner isolation.'),('Synchronization coordinator (planned)','Retry through authenticated Capture endpoint contracts. Procurement registration creates source expectation only; retries never authorize physical stock.'),('Reconciliation views (planned)','Present same-key changed payload conflict, missing dependencies, pending/rejected outcome and later receiving state without silently merging submissions.'),('CaptureOperation and existing idempotency contract','Existing capture entity and uniqueness for company/device/client operation. Current procurement endpoint replays same accepted canonical payload; conflict returns 409. General offline lifecycle remains target design.'),('Receiving projection integration','Use current Ledger receiving state after synchronization; PENDING_ARRIVAL or blocked discrepancy means zero usable stock, not a failed duplicate source record.')]
pkgs=[('04_packages_core','Backend Package Diagram',backend),('03_packages_workspace','Frontend Package Diagram (Website)',web),('02_packages_field','Frontend Package Diagram (Field Web Client)',field)]
(R/'tools/package_descriptions.json').write_text(json.dumps(pkgs,indent=2))
# Main architecture and package descriptions stay in the supplied sample structure.
start=md.index('**Component Explanations**');end=md.index('## 2. Database Design')
explanations=[
 ('Application topology','The source repository establishes a modular monolith: ASP.NET Core 10 host GrainLedger.Api with Identity, Catalogs, Capture and Ledger class-library modules, each owning a schema and migrations. Planned compliance/export capabilities remain in-process design responsibilities; their project split is not yet implemented.'),
 ('Frontend','apps/web is React + TypeScript + Vite. The same-origin production host serves its static build; development Vite proxies /api and /signin-google. Mobile-friendly web capture is the target surface for offline work; apps/mobile is currently a placeholder.'),
 ('Request and authorization boundary','Minimal API endpoint mappings invoke feature handlers, DbContexts or LedgerService. The repository does not implement a universal MVC Controller → Service → Repository stack. Persisted active role, named permission, customer owner and assigned facility are checked server-side. Browser cookies are HttpOnly; cookie mutations require X-CSRF-TOKEN. JWT Bearer access is a separate API-client path.'),
 ('Identity and external authentication','Identity owns Google authorization-code/PKCE handling, local email/password verification/recovery through configured SMTP, persisted sessions and permission/access changes. Google and SMTP are actual supported adapters. No provider credentials are exposed to React.'),
 ('Catalogs and Capture','Catalogs owns canonical commodities, units, reference attributes and MRL versions. Capture owns farmer/plot/season/spray and immutable procurement source expectations. Procurement registration commits a source package and pending-receiving projection; it does not create usable Ledger stock.'),
 ('Ledger and accepted arrivals','LedgerEndpoint handlers call LedgerService.CommandAsync, ReadAsync or ReceivingAsync. Npgsql invokes protected ledger.command, ledger.read and ledger.arrival_command routines. Complete actual arrival measurements either match exactly and post actual mass, or await assigned MillManager review. Only supported shortage approval posts actuals; surplus/zero/full-loss acceptance is not added.'),
 ('Persistence and transaction contract','PostgreSQL is the runtime store; shared Supabase is the configured team environment, not an extra business service. Source/stock changes share one connection and caller-owned READ COMMITTED transaction. Advisory lock 714928352 is acquired before source/stock rows; module routines do not commit a caller transaction. Current protected routines may own their enclosing statement transaction. Target reservations, approvals and CLOSE must join this protection rather than bypass it.'),
 ('Numbers, history and evidence','Mass crosses the API as decimal strings and is exactly numeric(20,6) in kg. Original quantity/unit and accepted observations remain retained. Source attribution uses integer micro-kg largest remainders, including allocation of each input share across root source contributions. Existing evidence content is retained in ledger.evidence with digest and scoped access; generic file evidence is not a completed laboratory residue model.'),
 ('Planned business capabilities','Lab/cohort bounds, residue propagation, export planning/approval, INPUT/OUTPUT reservations, loading, whole-container CLOSE/dossiers and complete container traceback remain full target design in Sections 3.5–3.8. A published MRL resolver and physical blending are existing building blocks, not proof that export compliance/planning is implemented. Lot HOLD exists; container HOLD and full downstream effects require the target extension.'),
 ('Build, deployment and external information','The Dockerfile builds the web frontend and .NET host into a container. CI/CD remains a delivery requirement, not a claimed existing pipeline vendor. Production uses same-origin HTTPS behind a trusted reverse proxy. Laboratory reports and regulatory MRL publications remain human-mediated information sources; no direct lab/regulatory connector is assumed.')]
body='**Component Explanations**\n\n'
for title,desc in explanations:body+='**'+title+':** '+desc+'\n\n'
body+='### 1.2 Package Diagram\n\nSolid groups describe observed source modules/files; dashed groups describe planned capabilities. File groups in the flat frontend are shown explicitly as source groups, not as directories that already exist. Dependencies cross module boundaries through in-process contracts; EF entities remain within their owning module.\n\n'
for j,(name,title,rows) in enumerate(pkgs,1):
 body+=f'#### 1.2.{j} {title}\n\n![{title}](diagrams/{name}.svg)\n\nDiagram {j+1}: {title}\n\n**Package Descriptions**\n\n'+table(['No','Package','Description'],[(f'{i:02}',x,y) for i,(x,y) in enumerate(rows,1)])+f'Table {j}: {title.replace("Diagram","Descriptions")}\n\n'
md=md[:start]+body+md[end:]
md=md.replace('revision 0.2','revision 0.3').replace('Interface names and methods are proposed design contracts; business rules and entity names follow the supplied GrainLedger SRS, use cases and ERD.', 'Repository context identifies existing technologies, modules, endpoint/persistence boundaries and receiving behavior. Unfinished workflows remain fully specified as planned target design, not removed or claimed complete. Source conflicts and exact file references are recorded in repository_context_vi.md.')
a=md.index('Class diagrams follow the example');b=md.index('### 3.1 ',a)
md=md[:a]+'''Class diagrams retain the example's feature-domain presentation. Existing CLR classes use observed C# names/properties; logical SQL models are explicitly identified because Ledger does not define an ORM entity for every business object. Planned classes/methods are target design. Property-only existing EF entities are not assigned fictional implemented business methods. Sequence lifelines use Minimal API endpoint mappings and actual DbContext/Npgsql boundaries where observed; future service/port names are labelled planned. Complete future scope is preserved regardless of implementation progress.\n\nFor source/stock commands, the global write advisory lock and READ COMMITTED contract apply; read-only list transactions need not use the stock-write contract. MRL publication has its own per-version/pair locking and ETag checks. Browser UI guards never replace persisted authorization.\n\n'''+md[b:]
statuses={1:'Repository-backed foundation: Identity/Catalogs administration, facility configuration and MRL publication exist. The complete target administration contract remains in scope.',2:'Mixed: field source registration, procurement idempotency and physical-arrival workflows exist; complete offline capture/sync and sample workflows remain planned. Registration and stock acceptance are separate outcomes.',3:'Mixed: physical processing, transfers, stock and source-share history exist through LedgerService/protected SQL; approved export-plan scope and reservations remain planned extensions.',4:'Repository-backed processing-correction flow with target accounting extensions. Current code binds reconciliation to current positions and exact replacement content/version. Export reservation reversal integration remains planned.',5:'Planned full compliance design. Published MRL maintenance/resolution exists in Catalogs; ledger.evidence is generic operational evidence. Neither implies completed lab/cohort/residue assessment.',6:'Planned full export planning/review/reservation design. ExportStaff is a persisted role; its presence does not imply completed order/planner APIs.',7:'Planned full container loading/CLOSE/dossier design, retaining all original quantity and assessment requirements.',8:'Mixed: explicit lot HOLD, scoped notices/handling and root-source contributions exist. Full source-to-container traversal, container HOLD and downstream authority invalidation remain planned.',9:'Mixed: existing scoped ledger.read history and module audit records; combined cross-module export/compliance history is the complete target extension.'}
for i,status in statuses.items():md=re.sub(r'(### 3\.'+str(i)+r' [^\n]+\n\n)',r'\1**Design status:** '+status+'\n\n',md,count=1)
# Remove outdated requirement-like statements in the receipt group and cross-link the current source contract.
a=md.index('### 3.2 ');b=md.index('### 3.3 ',a);section=md[a:b]
section=section.replace('Capture company-scoped farmer/plot/season records, declarations, positive intake lines and sample/handover references. Offline capture retains records locally; the server independently accepts each submitted operation.', 'Capture company-scoped farmer/plot/season records, spray declarations and positive procurement expectations. Accepted complete physical arrivals create actual stock later. Offline capture and sampling remain full target-design workflows.')
section=section.replace('only UC-09 creates receipt stock','the UC-09 receiving extension creates actual stock only after accepted arrival')
section=section.replace('Procurement Officer uses','PurchasingStaff uses')
# Update the first sequence title to match its real two-stage design.
section=section.replace('Accept a connected procurement receipt','Register procurement and accept physical arrival')
section=section.replace('A stable operation ID identifies one command, not a whole arbitrary offline batch.', 'A stable operation ID identifies one command, not a whole arbitrary offline batch. Source capture APPLIED does not mean physical receiving is accepted.')
md=md[:a]+section+md[b:]
# Classes observed in C# use actual names/properties. No repository writes are performed.
csfiles=[REPO/'backend/src/GrainLedger.Modules.Identity/Domain/User.cs',REPO/'backend/src/GrainLedger.Modules.Identity/Domain/Organization.cs',REPO/'backend/src/GrainLedger.Modules.Catalogs/CatalogDbContext.cs',* (REPO/'backend/src/GrainLedger.Modules.Capture').glob('*.cs')]
cs={}
for p in csfiles:
 txt=p.read_text()
 for m in re.finditer(r'public sealed class (\w+)\s*(?:\([^\n]*\))?\s*(?::[^\n{]+)?\s*\{',txt):
  pos=m.end();depth=1;end=pos
  while end<len(txt) and depth:
   depth+=(txt[end]=='{')-(txt[end]=='}');end+=1
  attrs=re.findall(r'public\s+(?:required\s+)?([\w?<>]+)\s+(\w+)\s*\{\s*get;',txt[pos:end])
  cs[m[1]]=attrs
observed=[['Organization','User','MrlVersion','MrlLimit','Commodity','MeasurementUnit'],['Farmer','Plot','CropSeason','PlotSeason','SprayDeclaration','SprayItem','Procurement','ProcurementItem','SourceLot','CaptureOperation']]
for idx,names in enumerate(observed):
 f=manifest['features'][idx];src='classDiagram\ndirection TB\n';rows=[]
 for j,name in enumerate(names,1):
  attrs=[(t,n) for t,n in cs[name] if n not in {'PasswordHash','ProviderSubject','Sessions','Users','Organization'}]
  # typed feature attributes rather than all account security metadata
  if name=='User':attrs=[x for x in attrs if x[1] in ['Id','Email','DisplayName','Role','OrganizationId','IsActive','Version']]
  else:attrs=attrs[:9]
  src+=f'class {name} {{\n <<existing CLR>>\n'+''.join(' +'+t.replace('?','Nullable')+' '+n+'\n' for t,n in attrs)+'}\n'
  rows.append((f'{j:02}',name,'; '.join(t+' '+n for t,n in attrs),'Existing property-only domain/EF model; behavior is in endpoint handlers, services or protected routines.'))
 if idx==0:src+='Organization "0..1" --> "0..*" User : current organization\nMrlVersion "1" --> "0..*" MrlLimit : limits\nCommodity "1" --> "0..*" MrlVersion : commodity\n'
 else:src+='Farmer "1" --> "0..*" Plot\nPlot "1" --> "0..*" PlotSeason\nCropSeason "1" --> "0..*" PlotSeason\nPlotSeason "1" --> "0..*" SprayDeclaration\nSprayDeclaration "1" --> "0..*" SprayItem\nProcurement "1" --> "1..*" ProcurementItem\nProcurementItem "1" --> "1" SourceLot : registered source identity\n'
 (D/(f['classdiag']+'.mmd')).write_text(src)
 # Replace only feature class-spec table, preserve caption, constraints and sequences.
 a=md.index(f'### 3.{idx+1} ');b=md.index(f'### 3.{idx+2} ',a)
 part=md[a:b];part=re.sub(r'\| No \| Class \| Attributes \| Operations \|\n.*?(?=Table \d+:)',table(['No','Class','Attributes','Implementation / behavior'],rows),part,flags=re.S)
 md=md[:a]+part+md[b:]
# All other feature class diagrams retain the complete target-domain model and are explicit about representation.
for idx,f in enumerate(manifest['features'][2:],3):
 p=D/(f['classdiag']+'.mmd');s=(OLD/'diagrams'/p.name).read_text()
 stereo='logical target' if idx in [3,4,8,9] else 'planned'
 s=re.sub(r'(class \w+ \{\n)',r'\1 <<'+stereo+'>>\n',s);p.write_text(s)
# Align transport/persistence lifelines, never claim fictional MVC/repository classes exist.
for f in manifest['features']:
 for sn,title in f['seqs']:
  p=D/(sn+'.mmd');s=(OLD/'diagrams'/p.name).read_text();section=int(f['id'])-4
  if section in [3,4,8,9]:
   s=re.sub(r'(participant Ctrl as )[^\n]+',r'\1:LedgerEndpoints',s)
   s=re.sub(r'(participant (?:S|H|T) as ):(?:LedgerService|CorrectionService|HoldService|TraceService|HistoryService)',r'\1:LedgerService',s)
   s=re.sub(r'(participant Repo as )[^\n]+',r'\1:Npgsql + protected SQL',s)
   s=re.sub(r'(participant R as )[^\n]+',r'\1:Npgsql + protected SQL',s)
   s=s.replace('Controller','Endpoints')
   # Existing generic conceptual methods remain planned internals; observed façade dispatch described in notes.
   s+=' Note over Ctrl,DB: Existing entry uses LedgerService CommandAsync / ReadAsync / ReceivingAsync.<br/>Detailed logical steps show required protected behavior, including planned extensions.\n'
  elif section in [5,6,7]:
   s=s.replace('Controller','Endpoints (planned)').replace('Repository','Persistence port (planned)')
   s+=' Note over DB: Full target design, not a claim of implemented export or residue APIs.\n'
  p.write_text(s)
# Update all domain/sequence status explanations and source references.
md=md.replace('Logical class names in Section 3 map through repositories to the existing physical schema.', 'Existing logical/CLR names map through the owning module DbContext or protected Ledger SQL. Supplied ERD/DB pictures remain conceptual references; current migration source contains later physical-arrival changes and is not redrawn here.')
md=md.replace('A port represents an interface whose adapter participates in the caller transaction.','A port represents an interface whose adapter participates in the caller transaction.')
(R/'GrainLedger_SDD.md').write_text(md)
manifest['revision']='0.3 — repository-informed full target design'
manifest['repository_context']=str(REPO);manifest['implementation_status']=statuses
manifest['features'][1]['seqs'][0][1]='Register procurement and accept physical arrival'
(R/'design_manifest.json').write_text(json.dumps(manifest,indent=2))
print('Repository-informed SDD content, existing CLR class specifications and explicit planned boundaries updated.')
