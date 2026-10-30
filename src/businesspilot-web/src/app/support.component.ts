import { CommonModule, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from './auth.service';

interface SupportWorkspace {
  aiConfigured: boolean; provider: string | null; setupMessage: string | null;
  monthlyLimit: number; messagesUsed: number; widgetEnabled: boolean; widgetKey: string | null;
}
interface KnowledgeItem {
  id: string; kind: 'FAQ' | 'Document'; title: string; question: string | null; answer: string | null;
  content: string; isApproved: boolean; updatedAtUtc: string;
}
interface SupportMessage { id: string; role: string; content: string; provider: string | null; createdAtUtc: string; }
interface SupportConversation {
  id: string; source: string; status: string; leadName: string | null; leadEmail: string | null;
  contactConsent: boolean; createdAtUtc: string; updatedAtUtc: string; messages: SupportMessage[];
}
interface ConversationSummary {
  id: string; source: string; status: string; leadName: string | null; leadEmail: string | null;
  contactConsent: boolean; createdAtUtc: string; updatedAtUtc: string; lastMessage: string | null;
}
interface WorkspaceResponse { workspace: SupportWorkspace; embedCode: string | null; }
interface MessageResponse { conversation: SupportConversation; messagesUsed: number; }

@Component({
  selector: 'bp-support', standalone: true, imports: [CommonModule, FormsModule, DatePipe],
  templateUrl: './support.component.html', styleUrl: './support.component.css'
})
export class SupportComponent implements OnInit {
  readonly workspace = signal<SupportWorkspace | null>(null);
  readonly knowledge = signal<KnowledgeItem[]>([]);
  readonly conversations = signal<ConversationSummary[]>([]);
  readonly conversation = signal<SupportConversation | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  embedCode = '';
  message = '';
  knowledgeDraft: { kind: 'FAQ' | 'Document'; title: string; question: string; answer: string; content: string; isApproved: boolean } = this.blankKnowledge();

  constructor(readonly auth: AuthService, private readonly http: HttpClient) {}

  ngOnInit(): void { this.load(); }
  canManage(): boolean { return ['Owner', 'Admin'].includes(this.auth.session()?.role ?? ''); }
  load(): void {
    this.loading.set(true); this.error.set('');
    this.http.get<WorkspaceResponse>('/api/support/workspace').subscribe({
      next: x => { this.workspace.set(x.workspace); this.embedCode = x.embedCode ?? ''; this.loading.set(false); },
      error: e => { this.error.set(e?.error?.message ?? 'AI support settings could not load.'); this.loading.set(false); }
    });
    this.http.get<KnowledgeItem[]>('/api/support/knowledge').subscribe({ next: x => this.knowledge.set(x), error: e => this.error.set(e?.error?.message ?? 'Knowledge base could not load.') });
    this.loadConversations();
  }
  loadConversations(): void {
    this.http.get<ConversationSummary[]>('/api/support/conversations').subscribe({ next: x => this.conversations.set(x), error: e => this.error.set(e?.error?.message ?? 'Conversation history could not load.') });
  }
  openConversation(id: string): void {
    this.http.get<SupportConversation>(`/api/support/conversations/${id}`).subscribe({ next: x => this.conversation.set(x), error: e => this.error.set(e?.error?.message ?? 'Conversation could not load.') });
  }
  newConversation(): void { this.conversation.set(null); this.message = ''; this.error.set(''); }
  sendMessage(): void {
    const text = this.message.trim(); if (!text || this.busy()) return;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    this.http.post<MessageResponse>('/api/support/messages', { conversationId: this.conversation()?.id ?? null, message: text }).subscribe({
      next: result => { this.conversation.set(result.conversation); this.message = ''; this.busy.set(false); this.loadConversations();
        const ws = this.workspace(); if (ws) this.workspace.set({ ...ws, messagesUsed: result.messagesUsed }); },
      error: e => { this.error.set(e?.error?.message ?? 'Support message could not be saved.'); this.busy.set(false); }
    });
  }
  escalate(): void {
    const current = this.conversation(); if (!current) return;
    this.http.post(`/api/support/conversations/${current.id}/escalate`, {}).subscribe({
      next: () => { this.conversation.set({ ...current, status: 'Escalated' }); this.notice.set('Conversation marked for team follow-up.'); this.loadConversations(); },
      error: e => this.error.set(e?.error?.message ?? 'Could not escalate this conversation.')
    });
  }
  saveKnowledge(): void {
    const draft = this.knowledgeDraft;
    const payload = { ...draft, content: draft.kind === 'FAQ' ? draft.answer.trim() : draft.content.trim() };
    this.busy.set(true); this.error.set('');
    this.http.post<KnowledgeItem>('/api/support/knowledge', payload).subscribe({
      next: item => { this.knowledge.update(items => [item, ...items]); this.knowledgeDraft = this.blankKnowledge(); this.busy.set(false); this.notice.set('Knowledge item saved.'); },
      error: e => { this.busy.set(false); this.error.set(e?.error?.message ?? 'Knowledge item could not be saved.'); }
    });
  }
  toggleApproval(item: KnowledgeItem): void {
    const payload = { kind: item.kind, title: item.title, question: item.question, answer: item.answer, content: item.content, isApproved: !item.isApproved };
    this.http.put<KnowledgeItem>(`/api/support/knowledge/${item.id}`, payload).subscribe({
      next: updated => this.knowledge.update(items => items.map(x => x.id === updated.id ? updated : x)),
      error: e => this.error.set(e?.error?.message ?? 'Knowledge approval could not be changed.')
    });
  }
  archiveKnowledge(item: KnowledgeItem): void {
    if (!confirm(`Archive “${item.title}”? It will no longer be used for AI answers.`)) return;
    this.http.delete(`/api/support/knowledge/${item.id}`).subscribe({
      next: () => this.knowledge.update(items => items.filter(x => x.id !== item.id)),
      error: e => this.error.set(e?.error?.message ?? 'Knowledge item could not be archived.')
    });
  }
  toggleWidget(): void {
    const current = this.workspace(); if (!current) return;
    this.http.put('/api/support/widget/enabled', { enabled: !current.widgetEnabled }).subscribe({
      next: () => this.workspace.set({ ...current, widgetEnabled: !current.widgetEnabled }),
      error: e => this.error.set(e?.error?.message ?? 'Widget settings could not be saved.')
    });
  }
  async copyEmbedCode(): Promise<void> {
    try { await navigator.clipboard.writeText(this.embedCode); this.notice.set('Embed code copied.'); }
    catch { this.error.set('Could not copy. Select the snippet and copy it manually.'); }
  }
  messageSource(message: SupportMessage): string {
    if (message.role === 'System') return message.provider === 'Configuration' ? 'Setup status' : message.provider === 'KnowledgeBase' ? 'Approved knowledge' : message.provider === 'PlanLimit' ? 'Plan limit' : message.provider === 'ProviderUnavailable' ? 'Provider status' : 'System';
    if (message.provider === 'LocalKnowledge') return 'Approved knowledge';
    return message.role === 'Assistant' ? `AI support · ${message.provider ?? 'configured provider'}` : message.role === 'Customer' ? 'Customer' : 'Team';
  }
  private blankKnowledge() { return { kind: 'FAQ' as const, title: '', question: '', answer: '', content: '', isApproved: false }; }
}
