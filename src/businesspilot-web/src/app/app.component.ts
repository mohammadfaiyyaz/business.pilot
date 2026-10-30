import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { AuthService } from './auth.service';
import { SupportComponent } from './support.component';
import { SupportBotComponent } from './support-bot.component';
import { BillingOverview, Customer, DashboardData, Expense, FinancialReport, InventoryMovement, Invoice, Product } from './models';

type View = 'overview' | 'invoices' | 'customers' | 'billing' | 'expenses' | 'products' | 'reports' | 'support';
type AuthMode = 'home' | 'login' | 'register';

@Component({
  selector: 'bp-root', standalone: true, imports: [CommonModule, FormsModule, CurrencyPipe, DatePipe, SupportComponent, SupportBotComponent],
  templateUrl: './app.component.html', styleUrl: './app.component.css'
})
export class AppComponent {
  readonly view = signal<View>('overview');
  readonly authMode = signal<AuthMode>('home');
  readonly loading = signal(false);
  readonly error = signal('');
  readonly dashboard = signal<DashboardData | null>(null);
  readonly billing = signal<BillingOverview | null>(null);
  readonly report = signal<FinancialReport | null>(null);
  readonly customers = signal<Customer[]>([]);
  readonly invoices = signal<Invoice[]>([]);
  readonly expenses = signal<Expense[]>([]);
  readonly products = signal<Product[]>([]);
  readonly lowStockCount = signal(0);
  readonly productMovements = signal<InventoryMovement[]>([]);
  readonly stockHistoryProduct = signal<Product | null>(null);
  readonly expenseTotal = signal(0);
  readonly showCustomerForm = signal(false);
  readonly showInvoiceForm = signal(false);
  readonly showExpenseForm = signal(false);
  readonly editingExpense = signal<Expense | null>(null);
  readonly showProductForm = signal(false);
  readonly editingProduct = signal<Product | null>(null);
  readonly showStockForm = signal(false);
  readonly adjustingProduct = signal<Product | null>(null);
  readonly paymentInvoice = signal<Invoice | null>(null);
  readonly chartView = signal<'invoiced' | 'received' | 'expenses'>('invoiced');
  readonly profileOpen = signal(false);
  readonly profileEditing = signal(false);
  readonly profileSaving = signal(false);
  readonly profileError = signal('');
  profileDraft = { name: '', email: '', organizationName: '' };
  email = ''; password = ''; name = ''; organizationName = '';
  customerSearch = '';
  invoiceSearch = ''; invoiceStatus = 'All';
  expenseSearch = '';
  productSearch = '';
  reportFrom = this.reportStart(); reportTo = this.today();
  dashboardFrom = this.dashboardStart(); dashboardTo = this.today();
  expenseFrom = ''; expenseTo = '';
  customerDraft = { name: '', email: '', phone: '', address: '', gstin: '' };
  invoiceDraft = { customerId: '', productId: '', description: '', quantity: 1, unitPrice: 0, taxRate: 0, issueDate: this.today(), dueDate: this.inSevenDays() };
  paymentDraft = { amount: 0, paidOn: this.today(), method: 'Bank transfer', reference: '' };
  expenseDraft = { category: 'Software', vendor: '', amount: 0, expenseDate: this.today(), description: '' };
  productDraft = { sku: '', name: '', description: '', costPrice: 0, sellingPrice: 0, taxRate: 0, stockQuantity: 0, lowStockThreshold: 5 };
  stockDraft = { quantity: 1, direction: 'Increase', reason: 'Stock received' };

  constructor(readonly auth: AuthService, private readonly http: HttpClient) {}

  setView(view: View): void { this.view.set(view); this.error.set(''); if (view === 'customers') this.loadCustomers(); if (view === 'invoices') this.loadInvoices(); if (view === 'billing') this.loadBilling(); if (view === 'expenses') this.loadExpenses(); if (view === 'products') this.loadProducts(); if (view === 'reports') this.loadReport(); }
  setAuthMode(mode: AuthMode): void { this.authMode.set(mode); this.error.set(''); }
  toggleProfile(): void { this.profileOpen.update(open => !open); }
  openProfileEditor(): void {
    const session = this.auth.session();
    if (!session) return;
    this.profileDraft = { name: session.name, email: session.email, organizationName: session.organizationName };
    this.profileError.set(''); this.profileOpen.set(false); this.profileEditing.set(true);
  }
  saveProfile(): void {
    this.profileSaving.set(true); this.profileError.set('');
    this.http.put<{ name: string; email: string; organizationName: string; role: string }>('/api/profile', this.profileDraft).subscribe({
      next: profile => {
        const session = this.auth.session();
        if (session) this.auth.session.set({ ...session, name: profile.name, email: profile.email, organizationName: profile.organizationName, role: profile.role });
        this.profileSaving.set(false); this.profileEditing.set(false);
      },
      error: e => { this.profileSaving.set(false); this.profileError.set(e?.error?.message ?? 'Profile could not be updated.'); }
    });
  }
  submitAuth(form: NgForm): void {
    if (form.invalid) {
      form.control.markAllAsTouched();
      if (this.authMode() === 'register' && form.controls['password']?.invalid) {
        this.error.set('Password needs 12+ characters with uppercase and lowercase letters, a number, and a symbol.');
      } else if (form.controls['email']?.invalid) {
        this.error.set('Enter a valid email address.');
      } else {
        this.error.set('Complete all required fields. Name and business name must each have at least 2 characters.');
      }
      return;
    }
    this.loading.set(true); this.error.set('');
    const request = this.authMode() === 'register'
      ? this.auth.register(this.name, this.email, this.password, this.organizationName)
      : this.auth.login(this.email, this.password);
    request.subscribe({ next: () => { this.loading.set(false); this.authMode.set('home'); this.loadDashboard(); }, error: e => {
      this.loading.set(false); this.error.set(this.authErrorMessage(e));
    } });
  }
  private authErrorMessage(error: { status?: number; error?: { message?: string } }): string {
    if (error.status === 0) return 'The API is not reachable at port 5080. Start the API, then try again.';
    if (error.status === 500 || error.status === 503)
      return 'The API is running but could not access its database. Check SQL Server or LocalDB and the configured connection string.';
    return error.error?.message ?? 'Login or registration failed. Please try again.';
  }
  logout(): void { this.profileOpen.set(false); this.auth.logout(); this.dashboard.set(null); this.authMode.set('home'); }
  loadDashboard(): void {
    this.http.get<DashboardData>('/api/dashboard', { params: { from: this.dashboardFrom, to: this.dashboardTo } })
      .subscribe({ next: x => this.dashboard.set(x),
        error: () => this.error.set('Dashboard could not load. Check the API connection.') });
  }
  loadCustomers(): void {
    this.http.get<{ items: Customer[]; total: number }>('/api/customers', { params: { q: this.customerSearch } })
      .subscribe({ next: x => this.customers.set(x.items), error: () => this.error.set('Customers could not load.') });
  }
  loadInvoices(): void {
    this.http.get<Invoice[]>('/api/invoices', { params: { q: this.invoiceSearch, status: this.invoiceStatus === 'All' ? '' : this.invoiceStatus } })
      .subscribe({ next: x => this.invoices.set(x), error: () => this.error.set('Invoices could not load.') });
  }
  loadBilling(): void { this.http.get<BillingOverview>('/api/billing').subscribe({ next: x => this.billing.set(x), error: e => this.error.set(e?.error?.message ?? 'Billing details could not load.') }); }
  startCheckout(planCode: string): void {
    if (this.loading()) return;
    this.loading.set(true); this.error.set('');
    this.http.post<{ subscriptionId: string; checkoutUrl: string }>('/api/billing/checkout', { planCode }).subscribe({
      next: checkout => { this.loading.set(false); window.location.assign(checkout.checkoutUrl); },
      error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Razorpay checkout could not be started.'); }
    });
  }
  loadReport(): void {
    this.http.get<FinancialReport>('/api/reports/summary', { params: { from: this.reportFrom, to: this.reportTo } })
      .subscribe({ next: x => this.report.set(x), error: e => this.error.set(e?.error?.message ?? 'Financial report could not load.') });
  }
  exportReport(type: 'sales' | 'payments' | 'expenses' | 'outstanding' | 'overdue' | 'monthly'): void {
    this.http.get(`/api/reports/export/${type}`, { params: { from: this.reportFrom, to: this.reportTo }, responseType: 'blob' })
      .subscribe({ next: file => { const link = document.createElement('a'); const url = URL.createObjectURL(file); link.href = url;
        link.download = `businesspilot-${type}-${this.reportFrom}-${this.reportTo}.csv`; link.click(); URL.revokeObjectURL(url); },
        error: () => this.error.set(`Could not export ${type} report.`) });
  }
  loadExpenses(): void {
    const params: Record<string, string> = { q: this.expenseSearch };
    if (this.expenseFrom) params['from'] = this.expenseFrom;
    if (this.expenseTo) params['to'] = this.expenseTo;
    this.http.get<{ items: Expense[]; total: number }>('/api/expenses', { params })
      .subscribe({ next: x => { this.expenses.set(x.items); this.expenseTotal.set(x.total); }, error: () => this.error.set('Expenses could not load.') });
  }
  loadProducts(): void {
    this.http.get<{ items: Product[]; lowStockCount: number }>('/api/products', { params: { q: this.productSearch } })
      .subscribe({ next: x => { this.products.set(x.items); this.lowStockCount.set(x.lowStockCount); }, error: () => this.error.set('Products could not load.') });
  }
  openNewProduct(): void {
    this.editingProduct.set(null); this.productDraft = { sku: '', name: '', description: '', costPrice: 0, sellingPrice: 0, taxRate: 0, stockQuantity: 0, lowStockThreshold: 5 }; this.showProductForm.set(true);
  }
  openEditProduct(product: Product): void {
    this.editingProduct.set(product); this.productDraft = { sku: product.sku, name: product.name, description: product.description ?? '', costPrice: product.costPrice, sellingPrice: product.sellingPrice, taxRate: product.taxRate, stockQuantity: product.stockQuantity, lowStockThreshold: product.lowStockThreshold }; this.showProductForm.set(true);
  }
  saveProduct(): void {
    this.loading.set(true); this.error.set(''); const current = this.editingProduct();
    const request = current ? this.http.put(`/api/products/${current.id}`, this.productDraft) : this.http.post('/api/products', this.productDraft);
    request.subscribe({ next: () => { this.loading.set(false); this.showProductForm.set(false); this.loadProducts(); },
      error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not save product.'); } });
  }
  openStockAdjustment(product: Product): void {
    this.adjustingProduct.set(product); this.stockDraft = { quantity: 1, direction: 'Increase', reason: 'Stock received' }; this.showStockForm.set(true);
  }
  adjustStock(): void {
    const product = this.adjustingProduct(); if (!product) return;
    this.loading.set(true); this.error.set('');
    this.http.post(`/api/products/${product.id}/stock-adjustments`, this.stockDraft).subscribe({ next: () => {
      this.loading.set(false); this.showStockForm.set(false); this.loadProducts();
    }, error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not adjust stock.'); } });
  }
  loadStockMovements(product: Product): void {
    this.http.get<InventoryMovement[]>(`/api/products/${product.id}/stock-movements`).subscribe({ next: x => { this.productMovements.set(x); this.stockHistoryProduct.set(product); }, error: () => this.error.set('Stock history could not load.') });
  }
  archiveProduct(id: string): void {
    if (!confirm('Archive this product? Its inventory history will be retained.')) return;
    this.http.delete(`/api/products/${id}`).subscribe({ next: () => this.loadProducts(), error: () => this.error.set('Could not archive product.') });
  }
  openNewExpense(): void { this.editingExpense.set(null); this.expenseDraft = { category: 'Software', vendor: '', amount: 0, expenseDate: this.today(), description: '' }; this.showExpenseForm.set(true); }
  openEditExpense(expense: Expense): void {
    this.editingExpense.set(expense); this.expenseDraft = { category: expense.category, vendor: expense.vendor ?? '', amount: expense.amount, expenseDate: expense.expenseDate, description: expense.description }; this.showExpenseForm.set(true);
  }
  saveExpense(): void {
    this.loading.set(true); this.error.set('');
    const current = this.editingExpense();
    const request = current ? this.http.put(`/api/expenses/${current.id}`, this.expenseDraft) : this.http.post('/api/expenses', this.expenseDraft);
    request.subscribe({ next: () => { this.loading.set(false); this.showExpenseForm.set(false); this.loadExpenses(); this.loadDashboard(); },
      error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not save expense.'); } });
  }
  archiveExpense(id: string): void {
    if (!confirm('Archive this expense?')) return;
    this.http.delete(`/api/expenses/${id}`).subscribe({ next: () => { this.loadExpenses(); this.loadDashboard(); }, error: () => this.error.set('Could not archive expense.') });
  }
  usagePercent(used: number, limit: number): number { return limit === 0 ? 0 : Math.min(100, Math.round(used / limit * 100)); }
  createCustomer(): void {
    this.loading.set(true); this.error.set('');
    this.http.post('/api/customers', this.customerDraft).subscribe({ next: () => {
      this.loading.set(false); this.showCustomerForm.set(false); this.customerDraft = { name: '', email: '', phone: '', address: '', gstin: '' }; this.loadCustomers(); this.loadDashboard();
    }, error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not save customer.'); } });
  }
  openNewCustomer(): void { this.customerDraft = { name: '', email: '', phone: '', address: '', gstin: '' }; this.showCustomerForm.set(true); }
  createInvoice(): void {
    this.loading.set(true); this.error.set('');
    const draft = this.invoiceDraft;
    this.http.post('/api/invoices', { customerId: draft.customerId, issueDate: draft.issueDate, dueDate: draft.dueDate, notes: null,
      items: [{ description: draft.description, quantity: Number(draft.quantity), unitPrice: Number(draft.unitPrice), taxRate: Number(draft.taxRate), productId: draft.productId || null }] })
      .subscribe({ next: () => { this.loading.set(false); this.showInvoiceForm.set(false); this.loadInvoices(); this.loadDashboard(); this.loadProducts(); },
        error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not create invoice.'); } });
  }
  openInvoiceForm(): void {
    this.invoiceDraft = { customerId: '', productId: '', description: '', quantity: 1, unitPrice: 0, taxRate: 0, issueDate: this.today(), dueDate: this.inSevenDays() };
    this.showInvoiceForm.set(true); this.loadCustomers(); this.loadProducts();
  }
  selectInvoiceProduct(): void {
    const product = this.products().find(x => x.id === this.invoiceDraft.productId);
    if (product) { this.invoiceDraft.description = product.name; this.invoiceDraft.unitPrice = product.sellingPrice; this.invoiceDraft.taxRate = product.taxRate; }
  }
  cancelInvoice(invoice: Invoice): void {
    if (!confirm(`Cancel invoice ${invoice.number}? Any stock issued from the catalog will be restored.`)) return;
    this.http.post(`/api/invoices/${invoice.id}/cancel`, {}).subscribe({ next: () => { this.loadInvoices(); this.loadDashboard(); this.loadProducts(); },
      error: e => this.error.set(e?.error?.message ?? 'Could not cancel invoice.') });
  }
  downloadInvoice(invoice: Invoice): void {
    this.error.set('');
    this.http.get(`/api/invoices/${invoice.id}/pdf`, { responseType: 'blob' }).subscribe({
      next: file => {
        const url = URL.createObjectURL(file);
        const link = document.createElement('a');
        link.href = url;
        link.download = `businesspilot-${invoice.number.replace(/[^a-z0-9_-]/gi, '-')}.pdf`;
        document.body.appendChild(link); link.click(); link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
      },
      error: e => this.error.set(e?.error?.message ?? 'Could not download this invoice PDF.')
    });
  }
  openPayment(invoice: Invoice): void {
    this.paymentDraft = { amount: invoice.balanceDue, paidOn: this.today(), method: 'Bank transfer', reference: '' };
    this.paymentInvoice.set(invoice);
  }
  recordPayment(): void {
    const invoice = this.paymentInvoice(); if (!invoice) return;
    this.loading.set(true); this.error.set('');
    this.http.post(`/api/invoices/${invoice.id}/payments`, this.paymentDraft).subscribe({ next: () => {
      this.loading.set(false); this.paymentInvoice.set(null); this.loadInvoices(); this.loadDashboard();
    }, error: e => { this.loading.set(false); this.error.set(e?.error?.message ?? 'Could not record payment.'); } });
  }
  archiveCustomer(id: string): void {
    if (!confirm('Archive this customer? Their invoice history will remain available.')) return;
    this.http.delete(`/api/customers/${id}`).subscribe({ next: () => this.loadCustomers(), error: () => this.error.set('Could not archive customer.') });
  }
  revenueHeight(amount: number): number {
    const values = this.dashboard()?.monthlyActivity.map(x => this.chartAmount(x)) ?? [];
    const max = Math.max(...values, 0);
    return max === 0 ? 0 : Math.max(4, Math.round(amount / max * 100));
  }
  chartAmount(month: DashboardData['monthlyActivity'][number]): number {
    return this.chartView() === 'invoiced' ? month.sales : this.chartView() === 'received' ? month.received : month.expenses;
  }
  chartHeight(month: DashboardData['monthlyActivity'][number]): number { return this.revenueHeight(this.chartAmount(month)); }
  chartLinePoints(): string {
    const rows = this.dashboard()?.monthlyActivity ?? [];
    return rows.map((row, index) => `${this.chartPointX(index, rows.length)},${this.chartPointY(this.chartAmount(row))}`).join(' ');
  }
  chartPointX(index: number, count: number): number { return count <= 1 ? 300 : Math.round(28 + index * 544 / (count - 1)); }
  chartPointY(amount: number): number {
    const values = this.dashboard()?.monthlyActivity.map(x => this.chartAmount(x)) ?? [];
    const max = Math.max(...values, 0);
    return max === 0 ? 124 : Math.round(124 - amount / max * 88);
  }
  trendText(current: number | undefined, change: number | null | undefined): string {
    if (change === null || change === undefined) return current ? 'New activity in this period' : 'No activity to compare';
    return `${change > 0 ? '+' : ''}${change}% vs previous period`;
  }
  statusDonutStyle(): string {
    const status = this.dashboard()?.paymentStatus;
    if (!status) return 'conic-gradient(#e8edf5 0deg 360deg)';
    const values = [status.paid, status.partiallyPaid, status.overdue, status.awaiting];
    const colors = ['#1d9b70', '#d69a31', '#d45e60', '#a4b1c5'];
    const total = values.reduce((sum, value) => sum + value, 0);
    if (!total) return 'conic-gradient(#e8edf5 0deg 360deg)';
    let angle = 0;
    const segments = values.map((value, i) => {
      const start = angle; angle += value / total * 360;
      return `${colors[i]} ${start}deg ${angle}deg`;
    });
    return `conic-gradient(${segments.join(',')})`;
  }
  private dashboardStart(): string { const d = new Date(); d.setDate(1); d.setMonth(d.getMonth() - 5); return d.toISOString().slice(0, 10); }
  private today(): string { return new Date().toISOString().slice(0, 10); }
  private inSevenDays(): string { const d = new Date(); d.setDate(d.getDate() + 7); return d.toISOString().slice(0, 10); }
  private reportStart(): string { const d = new Date(); d.setDate(1); d.setMonth(d.getMonth() - 5); return d.toISOString().slice(0, 10); }
}
