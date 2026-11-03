# Namovation workflow and implementation plan

## Purpose

Namovation is an employee innovation portal. Employees submit improvement ideas, an Innovate Manager screens and compiles submissions monthly, and three cross-functional Innovate Board members independently score shortlisted ideas. The manager compiles the scores; ideas averaging at least 40 out of 50 go to a board and manager meeting for reward or appreciation consideration.

## Roles and access

| Role | Can do |
| --- | --- |
| Employee | Submit multiple ideas; see own ideas and their status |
| Innovate Manager | See all submissions and submitter identity; screen and anonymize; forward monthly compilations; compile completed evaluations; record meeting outcomes; view repositories |
| Innovate Board | See only anonymized title, description, and system reference; submit one independent evaluation per idea |

ASP.NET Core Identity stores users and roles. On startup the application creates the Employee, InnovateManager, InnovateBoard, and Admin roles. A one-time Development-only page creates the first Admin account through Identity; the password is hashed before storage, and the page closes once an Admin exists. The admin can create users, assign roles, and require/allow authenticator setup for public self-registration from the Admin UI. This setting is persisted in a singleton PostgreSQL `RegistrationSettings` row and defaults to required. When required, new self-registrants enroll an authenticator app before receiving Employee access; Identity stores the secret and recovery codes in `AspNetUserTokens`. When optional, the authenticator step is skipped. The setting affects new self-registration only. In Development, Employee access is assigned after setup (or immediately when optional); in Production, an admin assigns the role after checking employee eligibility. Admin-created accounts are provisioned directly. Authenticator setup proves access to a TOTP app; it does not verify email ownership.

## Workflow mapping

1. **Submission:** A regular employee submits an idea. The original submission is kept with its owner and timestamp. The reference number is unique. The application permits multiple ideas per employee.
2. **Acknowledgement:** The specification calls for a thank-you email on receipt. An email provider and sender configuration are not supplied, so sending is an integration point and is not represented as working in this scaffold.
3. **Monthly screening:** The manager screens submitted ideas and records an optional rationale. Rejected ideas are retained in Repository 1. Shortlisted ideas are grouped by submission month in a monthly compilation.
4. **Anonymization and forwarding:** For each shortlisted item, the manager supplies a separate board-facing title and description after removing names, designations, locations, and other identifying details. The board pages query only these sanitized fields and the generated reference number; they do not query owner identity or the original free text. The manager forwards a month as a batch.
5. **Independent board scoring:** Each evaluation contains Novelty, Feasibility, Relevance, Business Impact, and Commercial Viability, each constrained to 1–10. A database unique key prevents one board member from scoring the same idea twice. Compilation waits for three distinct submissions.
6. **Manager compilation:** The average of the three evaluation totals is calculated on a 0–50 scale. Below 40 is classified in Repository 2. At or above 40 is marked eligible and awaits a meeting outcome; threshold qualification does not itself grant a reward.
7. **Meeting outcome:** The manager records Rewarded, Appreciated, or Not Selected with optional notes. Rewarded and Appreciated items go to Repository 3. An eligible item marked Not Selected remains compiled and unassigned to a repository because the source gives no repository for that outcome.

## Repository meaning

| Repository | Source meaning | Application behavior |
| --- | --- | --- |
| 1 | Ideas not qualified for board evaluation | Assigned at manager rejection |
| 2 | Board-evaluated ideas not qualified for reward | Assigned when average is below 40/50 |
| 3 | Ideas selected for reward | Assigned only after a meeting records Rewarded or Appreciated |

## Suggested database schema

The EF Core model maps the following tables (Identity's standard user, role, claim, login, and token tables are also included):

| Table | Key data and constraints |
| --- | --- |
| `Ideas` | Original title, description, optional problem/benefit, submitter user id, unique reference, submission time, status, optional monthly compilation id |
| `MonthlyCompilations` | Year, month, manager id, forwarded flag; unique year/month |
| `Screenings` | One per idea; manager, shortlist decision, rationale, anonymized board title and description |
| `BoardEvaluations` | Five scores, board member id, submitted time; unique idea/member pair |
| `IdeaCompilations` | One per idea; evaluation count, average total, eligibility, nullable repository, meeting outcome/notes, manager and time |
| `IdeaStatusEvents` | Append-only status history with actor, time, and note |

Important indexes and relationships are configured in `NamovationDbContext`. The startup scaffold creates the schema with EF Core `EnsureCreated`; production should move to reviewed EF migrations before schema evolution.

## Architecture

The solution is a modular monolith with four projects:

- **Namovation.Domain:** workflow entities, statuses, and score rules.
- **Namovation.Application:** workflow service contract and application exceptions.
- **Namovation.Infrastructure:** EF Core PostgreSQL persistence, Identity user profile, and workflow service implementation.
- **Namovation.Web:** ASP.NET Core MVC, Razor UI, Identity sign-in, role-based authorization, and DI composition.

This keeps workflow decisions in a service, persistence in Infrastructure, and the UI behind role-authorized MVC actions. The board-facing view model is deliberately separate from the employee and manager view models.

## Implementation plan

1. **Foundation (included):** Create solution projects; configure Identity, roles, EF Core, PostgreSQL, and domain entities.
2. **Employee portal (included):** Submit ideas and view own statuses.
3. **Manager screening (included):** Review, anonymize, reject to Repository 1, and prepare/forward monthly batches.
4. **Board review (included):** View sanitized ideas and submit five scores.
5. **Compilation and repositories (included):** Require three evaluations, calculate averages, apply threshold, record meeting outcomes, and classify repositories.
6. **Production operations (next):** Configure production database secrets, account provisioning, audit/retention policy, and deployment secrets. Continue using reviewed EF Core migrations for schema changes. Submission acknowledgements and automated workflow notifications remain integration tasks until the organization supplies an email provider, sender policy, and templates. Authenticator sign-in itself requires no SMTP provider or external API key.

## Decisions to confirm

These are left visible rather than treated as settled policy:

1. **Repository 3 wording:** The document calls Repository 3 “ideas selected for reward,” but says 40/50 only qualifies ideas for consideration and reserves the final reward/appreciation choice for a meeting. This scaffold interprets Repository 3 as finalized Rewarded/Appreciated ideas. A threshold-qualified idea marked Not Selected is kept in Compiled status without a repository until the organization defines its disposition.
2. **Screening criteria:** The document says the manager assesses suitability but gives no criteria. The app requires a manager decision and permits a rationale without inventing a scoring rubric.
3. **Incomplete/late board reviews:** The source requires three scores but gives no deadline, replacement-member, or missing-score rule. Compilation therefore stays blocked until three submissions exist.
4. **Email:** Sender, provider, templates, and retry behavior are unspecified. Submission and shortlist emails remain an integration task.
5. **User and board provisioning:** The source describes nominated users and three board members but does not say who creates accounts or changes membership. Admins create accounts and assign Manager and Board roles in the User accounts and roles screens. Assign the board role to the three nominated accounts only.
6. **Database environment:** PostgreSQL is the selected local provider, using the supplied `DefaultConnection`; provide credentials through a user secret or the `ConnectionStrings__DefaultConnection` environment variable.

## Run locally

Install the .NET 10 SDK and PostgreSQL with the server listening on port 5432. The local connection string is in the Git-ignored `Namovation.Web/appsettings.Development.json`. From the workspace root:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/work/dotnet-cli"
dotnet restore Namovation.slnx
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool install --global dotnet-ef --version 10.0.3 # only if dotnet-ef is not already installed
dotnet ef database update --project .\Namovation.Infrastructure --startup-project .\Namovation.Web
dotnet run --project .\Namovation.Web
```

Apply migrations with `dotnet ef database update` before starting the app. App startup creates Identity roles. Open the Development-only “Create first administrator” page from the home screen once to create the first Admin account. Sign in as that admin to create and manage accounts in the User accounts and roles screens. New Development registrations are assigned Employee access. Confirm employee eligibility (regular employee, grade C1 or above) and maintain exactly three nominated board members as part of that process.

When authenticator setup is required, users install Microsoft Authenticator or another TOTP-compatible app and enter the setup key shown by the application. No mail server, SMTP credentials, SMS provider, or API key is needed for authenticator enrollment or sign-in. A recovery-code page displays single-use backup codes once after enrollment; users should save them securely.
