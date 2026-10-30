# BusinessPilot

BusinessPilot is an invoicing and payment-tracking SaaS starter for Indian freelancers and small service businesses. The repository is a greenfield Angular + ASP.NET Core API + SQL Server application.

## Project layout and naming

```text
src/
  BusinessPilot.Api/   ASP.NET Core API
  businesspilot-web/   Angular frontend
```

The backend uses PascalCase for its project, folders, and C# files, matching .NET conventions. The Angular workspace uses kebab-case for its folder and npm package name. Keep feature files grouped by role under the API (`Controllers`, `Services`, `Data`, and `Models`) and use Angular's `*.component.*` suffix for component files. Build output and local tooling folders are ignored by Git.

## Current implementation

- Responsive public landing page and signed-in workspace shell
- Registration creates a user, organization, and Owner membership
- Password-based login with Identity password hashing, lockout, and API rate limiting
- JWT authentication for organization-scoped API requests
- SQL Server / EF Core models for users, organizations, subscriptions, customers, invoices, line items, payments, and expenses
- Organization-scoped product catalog with SKU uniqueness, stock adjustments, and inventory movement history
- Catalog invoice lines snapshot product SKU, description, price, and tax; issued invoices deduct stock and unpaid cancellation restores it
- Organization-scoped expense records with create, edit, archive, search, and date filters
- Customer CRUD with archive behavior
- Server-calculated invoice totals, invoice status, and manual payment recording
- Dashboard summary backed by organization data
- Dashboard expense totals and six-month invoiced, received, and expenses chart data from persisted records
- Role-protected financial report summaries with date ranges and formula-safe CSV exports for sales, payments, and expenses
- Angular forms for account setup, customers, and invoice creation
- Billing page with Free, Starter, and Pro plan usage; monthly invoice and active-customer limits are enforced by the API
- New accounts get a 14-day Free trial; after it expires, the workspace is read-only until a paid subscription is active
- Starter costs ₹499/month and Pro costs ₹1,499/month; Razorpay subscription checkout and signed webhooks update access

Invoice PDF export, email delivery, invitations, and forgot-password email flow are not implemented yet.

## Requirements

- .NET SDK 10
- Node.js 20.19+ (or a supported newer LTS release) and npm
- SQL Server or SQL Server LocalDB

## Configure the API

The example connection string uses Windows SQL Server LocalDB. Change it for your SQL Server installation if needed. Configure the variables from `.env.example` in your shell or IDE; ASP.NET Core reads the `__`-separated environment variable names as nested configuration.

From the repository root in PowerShell, configure the API and install/build the frontend:

```powershell
$env:ConnectionStrings__Default = 'Server=(localdb)\MSSQLLocalDB;Database=BusinessPilot;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true'
$env:Jwt__SigningKey = 'replace-this-with-a-random-secret-at-least-32-characters-long'
```

The initial SQL Server migration is included. Install the EF command-line tool and apply the migration to your configured database:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/BusinessPilot.Api
npm install
npm run build
dotnet build src/BusinessPilot.Api/BusinessPilot.Api.csproj
```

## Configure live Razorpay billing

Create monthly Razorpay Subscription Plans in the same Razorpay mode (Test or Live) as the API keys: Starter at ₹499/month and Pro at ₹1,499/month. Put the resulting plan IDs and API credentials in the API host's environment or secret manager; do not commit secrets:

```text
Billing__Provider=Razorpay
Billing__TrialDays=14
Razorpay__KeyId=<key id>
Razorpay__KeySecret=<key secret>
Razorpay__WebhookSecret=<webhook secret>
Razorpay__PlanIds__Starter=<starter plan id>
Razorpay__PlanIds__Pro=<pro plan id>
```

Configure a public HTTPS Razorpay webhook URL at `/api/billing/webhook` for subscription activation, charge, cancellation, halt, and pending events. The API verifies each webhook signature before changing a workspace subscription. Paid-plan buttons remain disabled until keys and both plan IDs are present. Test subscriptions and webhook delivery in Razorpay Test mode before switching the API and webhook to Live mode.

Run the API in one terminal:

```powershell
npm run start:api
```

The API listens at `http://localhost:5080`. Its OpenAPI document is available at `/openapi/v1.json` in Development.

## Configure and run Angular

```powershell
npm start
```

The Angular dev server runs at `http://localhost:4200` and proxies `/api` requests to the API on port 5080.

## API endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET|POST /api/customers`
- `GET|PUT|DELETE /api/customers/{id}` (`DELETE` archives the customer)
- `GET|POST /api/invoices`
- `GET /api/invoices/{id}`
- `POST /api/invoices/{id}/payments`
- `GET /api/dashboard`
- `GET /api/health`
- `GET /api/billing`
- `POST /api/billing/checkout`
- `POST /api/billing/webhook` (Razorpay server-to-server)

All business endpoints require a bearer token. Data queries are scoped using the organization ID in the authenticated token.

## Security notes

Never deploy with the development signing key in `appsettings.json`; supply a unique, random `Jwt__SigningKey` through a secret manager. Production requires HTTPS and an approved CORS origin. Keep Razorpay secrets in the API host's secret manager and validate live webhook delivery before launch. Do not store payment-card details.
