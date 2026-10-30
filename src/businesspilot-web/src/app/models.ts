export interface AuthResponse {
  token: string; expiresAtUtc: string; name: string; email: string; organizationId: string; organizationName: string; role: string;
}
export interface Customer {
  id: string; name: string; email?: string | null; phone?: string | null; address?: string | null; gstin?: string | null;
}
export interface Invoice {
  id: string; number: string; customerId: string; customerName: string; issueDate: string; dueDate: string; status: string;
  subtotal: number; taxTotal: number; total: number; amountPaid: number; balanceDue: number;
}
export interface Expense {
  id: string; category: string; vendor?: string | null; amount: number; expenseDate: string; description: string;
  isArchived: boolean; createdAtUtc: string; updatedAtUtc: string;
}
export interface Product {
  id: string; sku: string; name: string; description?: string | null; costPrice: number; sellingPrice: number; taxRate: number;
  stockQuantity: number; lowStockThreshold: number; isLowStock: boolean; createdAtUtc: string; updatedAtUtc: string;
}
export interface InventoryMovement {
  id: string; quantityDelta: number; reason: string; createdAtUtc: string;
}
export interface DashboardData {
  from: string; to: string; totalSales: number; paymentsReceived: number; totalExpenses: number; outstanding: number;
  overdueCount: number; overdueBalance: number; invoiceCount: number;
  salesChangePercent: number | null; paymentsChangePercent: number | null; expensesChangePercent: number | null;
  recentInvoices: Invoice[];
  recentCustomers: { id: string; name: string; email: string | null; createdAtUtc: string }[];
  lowStockProducts: { id: string; sku: string; name: string; stockQuantity: number; lowStockThreshold: number }[];
  paymentStatus: { paid: number; partiallyPaid: number; overdue: number; awaiting: number };
  recentActivity: { date: string; type: string; description: string; amount: number }[];
  monthlyActivity: { month: string; label: string; sales: number; received: number; expenses: number }[];
}
export interface PlanOffer {
  code: string; name: string; monthlyPriceInr: number | null; monthlyInvoiceLimit: number; customerLimit: number; userLimit: number;
}
export interface BillingOverview {
  planCode: string; planName: string; subscriptionStatus: string; startedAtUtc: string;
  invoicesUsedThisMonth: number; monthlyInvoiceLimit: number; customersUsed: number; customerLimit: number;
  usersUsed: number; userLimit: number; paymentProvider: string; checkoutConfigured: boolean;
  setupMessage: string | null; trialEndsAtUtc: string | null; trialDaysRemaining: number; plans: PlanOffer[];
}
export interface FinancialReport {
  from: string; to: string; totalSales: number; paymentsReceived: number; totalExpenses: number; netCashFlow: number;
  outstanding: number; outstandingInvoiceCount: number; overdueBalance: number; overdueInvoiceCount: number;
  invoiceCount: number; paymentCount: number; expenseCount: number;
  monthly: { month: string; label: string; sales: number; payments: number; expenses: number }[];
}
