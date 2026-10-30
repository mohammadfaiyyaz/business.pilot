import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, signal, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from './auth.service';

interface BotMessage { id: string; role: string; content: string; createdAtUtc: string; }
interface BotConversation { id: string; status: string; messages: BotMessage[]; }
interface BotReply { conversation: BotConversation; messagesUsed: number; }
interface BotWorkspace { aiConfigured: boolean; setupMessage: string | null; approvedKnowledgeCount: number; }

@Component({
  selector: 'bp-support-bot', standalone: true, imports: [CommonModule, FormsModule],
  templateUrl: './support-bot.component.html', styleUrl: './support-bot.component.css'
})
export class SupportBotComponent {
  readonly open = signal(false);
  readonly busy = signal(false);
  readonly conversation = signal<BotConversation | null>(null);
  readonly workspace = signal<BotWorkspace | null>(null);
  readonly error = signal('');
  message = '';
  @ViewChild('messageInput') messageInput?: ElementRef<HTMLTextAreaElement>;

  constructor(readonly auth: AuthService, private readonly http: HttpClient) {}

  toggle(): void {
    const opening = !this.open();
    if (opening) this.error.set('');
    this.open.set(opening);
    if (this.open() && !this.workspace()) {
      this.http.get<BotWorkspace>('/api/support/workspace').subscribe({
        next: value => this.workspace.set(value),
        error: () => this.error.set('Support is temporarily unavailable. Please try again later.')
      });
    }
    if (this.open()) setTimeout(() => this.messageInput?.nativeElement.focus(), 80);
  }

  startNew(): void { this.conversation.set(null); this.message = ''; this.error.set(''); }

  onEnter(event: KeyboardEvent): void {
    if (!event.shiftKey) { event.preventDefault(); this.send(); }
  }

  send(): void {
    const message = this.message.trim();
    if (!message || this.busy()) return;
    this.busy.set(true); this.error.set('');
    this.http.post<BotReply>('/api/support/messages', { conversationId: this.conversation()?.id ?? null, message }).subscribe({
      next: reply => { this.conversation.set(reply.conversation); this.message = ''; this.busy.set(false); },
      error: e => { this.error.set(e?.error?.message ?? 'Your message could not be sent. Try again.'); this.busy.set(false); }
    });
  }

  escalate(): void {
    const current = this.conversation();
    if (!current) return;
    this.http.post(`/api/support/conversations/${current.id}/escalate`, {}).subscribe({
      next: () => this.conversation.set({ ...current, status: 'Escalated' }),
      error: e => this.error.set(e?.error?.message ?? 'Could not send this conversation to the team.')
    });
  }
}
