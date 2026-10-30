using System.Data;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class SupportService(BusinessPilotDbContext db, BillingService billing, ISupportAiProvider ai)
{
    private const string UnsupportedAnswer = "I couldn't find that information in this organization's approved support materials. I can pass this to the team for help.";

    public async Task<object?> GetWorkspaceAsync(Guid organizationId, string userId, bool canManage, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return null;
        var widget = await EnsureWidgetAsync(organizationId, ct);
        var period = CurrentPeriod();
        var usage = await db.SupportMonthlyUsage.AsNoTracking().FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.PeriodStart == period, ct);
        var approvedKnowledgeCount = await db.SupportKnowledgeItems.AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && x.IsApproved && !x.IsArchived, ct);
        return new {
            aiConfigured = ai.IsConfigured,
            provider = ai.IsConfigured ? ai.ProviderName : null,
            setupMessage = ai.IsConfigured ? null : "AI integration requires configuration.",
            approvedKnowledgeCount,
            monthlyLimit = await billing.GetAiMessageLimitAsync(organizationId, ct),
            messagesUsed = usage?.MessagesUsed ?? 0,
            widgetEnabled = widget.IsEnabled,
            widgetKey = canManage ? widget.PublicKey : null
        };
    }

    public async Task<IReadOnlyList<object>?> ListKnowledgeAsync(Guid organizationId, string userId, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return null;
        return await db.SupportKnowledgeItems.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived)
            .OrderByDescending(x => x.UpdatedAtUtc).Select(x => (object)new {
                x.Id, x.Kind, x.Title, x.Question, x.Answer, x.Content, x.IsApproved, x.CreatedAtUtc, x.UpdatedAtUtc
            }).ToListAsync(ct);
    }

    public async Task<ServiceResult<object>> CreateKnowledgeAsync(Guid organizationId, string userId, SupportKnowledgeRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return ServiceResult<object>.Missing();
        if (request.Kind == "FAQ" && (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer)))
            return ServiceResult<object>.Failure("FAQs need both a question and an answer.");
        var item = new SupportKnowledgeItem { OrganizationId = organizationId, Kind = request.Kind, Title = request.Title.Trim(),
            Question = request.Question?.Trim(), Answer = request.Answer?.Trim(), Content = request.Content.Trim(), IsApproved = request.IsApproved };
        db.SupportKnowledgeItems.Add(item); await db.SaveChangesAsync(ct);
        return ServiceResult<object>.Success(KnowledgeDto(item));
    }

    public async Task<ServiceResult<object>> UpdateKnowledgeAsync(Guid organizationId, string userId, Guid id, SupportKnowledgeRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return ServiceResult<object>.Missing();
        if (request.Kind == "FAQ" && (string.IsNullOrWhiteSpace(request.Question) || string.IsNullOrWhiteSpace(request.Answer)))
            return ServiceResult<object>.Failure("FAQs need both a question and an answer.");
        var item = await db.SupportKnowledgeItems.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived, ct);
        if (item is null) return ServiceResult<object>.Missing();
        item.Kind = request.Kind; item.Title = request.Title.Trim(); item.Question = request.Question?.Trim();
        item.Answer = request.Answer?.Trim(); item.Content = request.Content.Trim(); item.IsApproved = request.IsApproved; item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<object>.Success(KnowledgeDto(item));
    }

    public async Task<bool> ArchiveKnowledgeAsync(Guid organizationId, string userId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return false;
        var item = await db.SupportKnowledgeItems.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived, ct);
        if (item is null) return false;
        item.IsArchived = true; item.IsApproved = false; item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return true;
    }

    public async Task<IReadOnlyList<object>?> ListConversationsAsync(Guid organizationId, string userId, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return null;
        var conversations = await db.SupportConversations.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.UpdatedAtUtc).Take(100)
            .Select(x => new {
                x.Id, x.Source, x.Status, x.LeadName, x.LeadEmail, x.ContactConsent, x.CreatedAtUtc, x.UpdatedAtUtc,
            }).ToListAsync(ct);
        var ids = conversations.Select(x => x.Id).ToArray();
        var latestMessages = ids.Length == 0 ? [] : await db.SupportMessages.AsNoTracking()
            .Where(x => ids.Contains(x.ConversationId)).OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.ConversationId, x.Content }).ToListAsync(ct);
        var lastByConversation = latestMessages.GroupBy(x => x.ConversationId)
            .ToDictionary(x => x.Key, x => x.First().Content);
        return conversations.Select(x => (object)new {
            x.Id, x.Source, x.Status, x.LeadName, x.LeadEmail, x.ContactConsent, x.CreatedAtUtc, x.UpdatedAtUtc,
            LastMessage = lastByConversation.GetValueOrDefault(x.Id)
        }).ToList();
    }

    public async Task<object?> GetConversationAsync(Guid organizationId, string userId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return null;
        var conversation = await db.SupportConversations.AsNoTracking().Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);
        return conversation is null ? null : ConversationDto(conversation);
    }

    public async Task<ServiceResult<object>> SendInAppAsync(Guid organizationId, string userId, SupportMessageRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return ServiceResult<object>.Missing();
        SupportConversation conversation;
        if (request.ConversationId is Guid id)
        {
            conversation = await db.SupportConversations.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
                ?? null!;
            if (conversation is null) return ServiceResult<object>.Missing();
            if (conversation.Status == "Closed") return ServiceResult<object>.Failure("This conversation is closed. Start a new one to continue.");
        }
        else
        {
            conversation = new SupportConversation { OrganizationId = organizationId, Source = "InApp" };
            db.SupportConversations.Add(conversation);
        }
        await SaveQuestionAsync(conversation, request.Message, ct);
        await AddSupportAnswerAsync(conversation, request.Message, ct);
        return ServiceResult<object>.Success(await ConversationResponseAsync(conversation.Id, organizationId, ct));
    }

    public async Task<bool> EscalateAsync(Guid organizationId, string userId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return false;
        var conversation = await db.SupportConversations.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);
        if (conversation is null) return false;
        if (conversation.Status != "Closed") conversation.Status = "Escalated";
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetWidgetEnabledAsync(Guid organizationId, string userId, bool enabled, CancellationToken ct)
    {
        if (!await IsMemberAsync(organizationId, userId, ct)) return false;
        var widget = await EnsureWidgetAsync(organizationId, ct);
        widget.IsEnabled = enabled; await db.SaveChangesAsync(ct); return true;
    }

    public async Task<object?> GetPublicWidgetAsync(string publicKey, CancellationToken ct)
    {
        var widget = await db.SupportWidgets.AsNoTracking().Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.PublicKey == publicKey && x.IsEnabled, ct);
        return widget is null ? null : new { organizationName = widget.Organization.Name, isEnabled = true, aiConfigured = ai.IsConfigured };
    }

    public async Task<object?> GetPublicConversationAsync(string publicKey, Guid conversationId, CancellationToken ct)
    {
        var widget = await db.SupportWidgets.AsNoTracking().FirstOrDefaultAsync(x => x.PublicKey == publicKey && x.IsEnabled, ct);
        if (widget is null) return null;
        var conversation = await db.SupportConversations.AsNoTracking().Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.OrganizationId == widget.OrganizationId && x.Source == "Widget", ct);
        return conversation is null ? null : new {
            conversation.Id, conversation.Status,
            Messages = conversation.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new { m.Id, m.Role, m.Content, m.CreatedAtUtc })
        };
    }

    public async Task<ServiceResult<object>> SendWidgetMessageAsync(string publicKey, WidgetSupportMessageRequest request, CancellationToken ct)
    {
        var widget = await db.SupportWidgets.AsNoTracking().FirstOrDefaultAsync(x => x.PublicKey == publicKey && x.IsEnabled, ct);
        if (widget is null) return ServiceResult<object>.Missing();
        if (request.ConsentToContact && (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email)))
            return ServiceResult<object>.Failure("Provide a name and email address to consent to follow-up.");
        SupportConversation conversation;
        if (request.ConversationId is Guid id)
        {
            conversation = await db.SupportConversations.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == widget.OrganizationId && x.Source == "Widget", ct)
                ?? null!;
            if (conversation is null) return ServiceResult<object>.Missing();
            if (conversation.Status == "Closed") return ServiceResult<object>.Failure("This conversation is closed.");
        }
        else
        {
            conversation = new SupportConversation { OrganizationId = widget.OrganizationId, Source = "Widget",
                VisitorKey = request.VisitorKey?.Trim() };
            db.SupportConversations.Add(conversation);
        }
        if (request.ConsentToContact)
        {
            conversation.ContactConsent = true;
            conversation.LeadName = request.Name!.Trim();
            conversation.LeadEmail = request.Email!.Trim();
        }
        await SaveQuestionAsync(conversation, request.Message, ct);
        await AddSupportAnswerAsync(conversation, request.Message, ct);
        return ServiceResult<object>.Success(await ConversationResponseAsync(conversation.Id, widget.OrganizationId, ct));
    }

    public async Task<bool> EscalateWidgetAsync(string publicKey, Guid conversationId, CancellationToken ct)
    {
        var widget = await db.SupportWidgets.AsNoTracking().FirstOrDefaultAsync(x => x.PublicKey == publicKey && x.IsEnabled, ct);
        if (widget is null) return false;
        var conversation = await db.SupportConversations.FirstOrDefaultAsync(x => x.Id == conversationId && x.OrganizationId == widget.OrganizationId && x.Source == "Widget", ct);
        if (conversation is null) return false;
        if (conversation.Status != "Closed") conversation.Status = "Escalated";
        conversation.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return true;
    }

    private async Task AddSupportAnswerAsync(SupportConversation conversation, string question, CancellationToken ct)
    {
        var evidence = await RetrieveEvidenceAsync(conversation.OrganizationId, question, ct);
        if (evidence.Count == 0)
        {
            await SaveAnswerAsync(conversation, BuildNoEvidenceAnswer(question), "KnowledgeBase", ct);
            return;
        }
        if (!ai.IsConfigured)
        {
            await SaveAnswerAsync(conversation, BuildLocalKnowledgeAnswer(evidence), "LocalKnowledge", ct);
            return;
        }
        var limit = await billing.GetAiMessageLimitAsync(conversation.OrganizationId, ct);
        if (!await ReserveUsageAsync(conversation.OrganizationId, limit, ct))
        {
            await SaveAnswerAsync(conversation, "This workspace has reached its monthly AI support message limit. Please escalate this conversation for help.", "PlanLimit", ct);
            return;
        }
        try
        {
            var answer = await ai.AnswerAsync(question, evidence, ct);
            await SaveAnswerAsync(conversation, answer, ai.ProviderName, ct);
        }
        catch (SupportProviderRateLimitException)
        {
            await ReleaseUsageAsync(conversation.OrganizationId, ct);
            await SaveAnswerAsync(conversation, "The AI provider is rate-limited right now. Please try again shortly or escalate this conversation.", "ProviderUnavailable", ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await ReleaseUsageAsync(conversation.OrganizationId, ct);
            await SaveAnswerAsync(conversation, "The AI provider is temporarily unavailable. Your message is saved; please try again or escalate this conversation.", "ProviderUnavailable", ct);
        }
    }

    private async Task<List<KnowledgeEvidence>> RetrieveEvidenceAsync(Guid organizationId, string question, CancellationToken ct)
    {
        var words = Tokens(question);
        if (words.Count == 0) return [];
        var items = await db.SupportKnowledgeItems.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.IsApproved && !x.IsArchived)
            .Select(x => new { x.Kind, x.Title, x.Question, x.Answer, x.Content }).ToListAsync(ct);
        return items.Select(item => {
                var text = $"{item.Title}\n{item.Question}\n{item.Answer}\n{item.Content}";
                var itemWords = Tokens(text);
                var score = words.Count(itemWords.Contains);
                return new { item.Kind, item.Title, item.Answer, item.Content, Text = text, Score = score };
            }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(4)
            .Select(x => new KnowledgeEvidence(x.Title, x.Text, x.Kind == "FAQ" ? x.Answer : null, x.Kind == "Document" ? x.Content : null)).ToList();
    }

    private static string BuildLocalKnowledgeAnswer(IReadOnlyList<KnowledgeEvidence> evidence)
    {
        var faqAnswer = evidence.Select(x => x.Answer).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(faqAnswer)) return faqAnswer.Trim();

        var document = evidence.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Content));
        if (document is null) return UnsupportedAnswer;
        var content = document.Content!.Trim();
        if (content.Length > 1200) content = content[..1200].TrimEnd() + "…";
        return $"From {document.Title}:\n{content}";
    }

    private static string BuildNoEvidenceAnswer(string question)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(question.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ").Trim();
        if (normalized is "hi" or "hello" or "hey" or "good morning" or "good afternoon" or "good evening" or "namaste")
            return "Hi! I’m your support assistant. Ask me about your business’s approved support information, or add FAQs in Support settings so I can help with more questions.";
        if (normalized is "thanks" or "thank you" or "thankyou" or "thx")
            return "You’re welcome! Ask another question whenever you need help.";
        return UnsupportedAnswer;
    }

    private async Task<bool> ReserveUsageAsync(Guid organizationId, int limit, CancellationToken ct)
    {
        if (limit <= 0) return false;
        var period = CurrentPeriod();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var usage = await db.SupportMonthlyUsage.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.PeriodStart == period, ct);
        if (usage is null) { usage = new SupportMonthlyUsage { OrganizationId = organizationId, PeriodStart = period }; db.SupportMonthlyUsage.Add(usage); }
        if (usage.MessagesUsed >= limit) return false;
        usage.MessagesUsed++; usage.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return true;
    }

    private async Task ReleaseUsageAsync(Guid organizationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var usage = await db.SupportMonthlyUsage.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.PeriodStart == CurrentPeriod(), ct);
        if (usage is not null && usage.MessagesUsed > 0) { usage.MessagesUsed--; usage.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        await transaction.CommitAsync(ct);
    }

    private async Task SaveQuestionAsync(SupportConversation conversation, string message, CancellationToken ct)
    {
        await SaveConversationMessageAsync(conversation, "Customer", message.Trim(), null, ct);
    }

    private async Task SaveAnswerAsync(SupportConversation conversation, string message, string provider, CancellationToken ct)
    {
        var role = provider is "Configuration" or "KnowledgeBase" or "PlanLimit" or "ProviderUnavailable" ? "System" : "Assistant";
        await SaveConversationMessageAsync(conversation, role, message, provider, ct);
    }

    private async Task SaveConversationMessageAsync(SupportConversation conversation, string role, string content, string? provider, CancellationToken ct)
    {
        var conversationEntry = db.Entry(conversation);
        if (conversationEntry.State == EntityState.Added)
            await db.SaveChangesAsync(ct);
        if (conversationEntry.State != EntityState.Detached)
            conversationEntry.State = EntityState.Detached;

        db.SupportMessages.Add(new SupportMessage {
            ConversationId = conversation.Id, Role = role, Provider = provider, Content = content
        });
        await db.SaveChangesAsync(ct);

        var updatedAt = DateTime.UtcNow;
        await db.SupportConversations.Where(x => x.Id == conversation.Id && x.OrganizationId == conversation.OrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAtUtc, updatedAt), ct);
        conversation.UpdatedAtUtc = updatedAt;
    }

    private async Task<object> ConversationResponseAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        var conversation = await db.SupportConversations.AsNoTracking().Include(x => x.Messages)
            .FirstAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);
        var messagesUsed = await db.SupportMonthlyUsage.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.PeriodStart == CurrentPeriod())
            .Select(x => (int?)x.MessagesUsed).FirstOrDefaultAsync(ct) ?? 0;
        return new { conversation = ConversationDto(conversation), messagesUsed };
    }

    private async Task<SupportWidget> EnsureWidgetAsync(Guid organizationId, CancellationToken ct)
    {
        var widget = await db.SupportWidgets.FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        if (widget is not null) return widget;
        widget = new SupportWidget { OrganizationId = organizationId };
        db.SupportWidgets.Add(widget); await db.SaveChangesAsync(ct);
        return widget;
    }

    private Task<bool> IsMemberAsync(Guid organizationId, string userId, CancellationToken ct)
        => db.Memberships.AsNoTracking().AnyAsync(x => x.OrganizationId == organizationId && x.UserId == userId, ct);

    private static DateOnly CurrentPeriod()
    {
        var now = DateTime.UtcNow;
        return new DateOnly(now.Year, now.Month, 1);
    }

    private static HashSet<string> Tokens(string text)
        => System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}")
            .Select(x => x.Value).ToHashSet(StringComparer.Ordinal);

    private static object KnowledgeDto(SupportKnowledgeItem x) => new { x.Id, x.Kind, x.Title, x.Question, x.Answer, x.Content, x.IsApproved, x.CreatedAtUtc, x.UpdatedAtUtc };

    private static object ConversationDto(SupportConversation x) => new {
        x.Id, x.Source, x.Status, x.LeadName, x.LeadEmail, x.ContactConsent, x.CreatedAtUtc, x.UpdatedAtUtc,
        Messages = x.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new { m.Id, m.Role, m.Content, m.Provider, m.CreatedAtUtc })
    };
}
