using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SharpClaw.Services;

namespace SharpClaw.Presentation;

public sealed partial class MainPage : Page
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private Guid? _selectedChannelId;
    private Guid? _selectedThreadId;
    private Guid? _selectedAgentId;
    private bool _pendingNewThread;
    private bool _suppressThreadSelection;
    private readonly Dictionary<Guid, bool> _expandedContexts = [];
    private List<AgentDto> _allAgents = [];

    public MainPage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.Services is null) return;
        await LoadSidebarAsync();
        await LoadAgentsAsync(null, null);
        UpdateCursor();
    }

    // ── Sidebar ──────────────────────────────────────────────────

    private async Task LoadSidebarAsync()
    {
        var api = App.Services!.GetRequiredService<SharpClawApiClient>();

        List<ContextDto>? contexts = null;
        List<ChannelDto>? channels = null;

        try
        {
            var ctxResp = await api.GetAsync("/channel-contexts");
            if (ctxResp.IsSuccessStatusCode)
                contexts = JsonSerializer.Deserialize<List<ContextDto>>(
                    await ctxResp.Content.ReadAsStringAsync(), Json);

            var chResp = await api.GetAsync("/channels");
            if (chResp.IsSuccessStatusCode)
                channels = JsonSerializer.Deserialize<List<ChannelDto>>(
                    await chResp.Content.ReadAsStringAsync(), Json);
        }
        catch { /* API not reachable — leave empty */ }

        contexts ??= [];
        channels ??= [];

        var contextChannelIds = new HashSet<Guid>();
        SidebarPanel.Children.Clear();

        // Group channels under their context, sorted by newest channel
        foreach (var ctx in contexts.OrderByDescending(c =>
            channels.Where(ch => ch.ContextId == c.Id).Max(ch => (DateTimeOffset?)ch.CreatedAt) ?? c.CreatedAt))
        {
            var ctxChannels = channels
                .Where(ch => ch.ContextId == ctx.Id)
                .OrderByDescending(ch => ch.CreatedAt)
                .ToList();

            foreach (var ch in ctxChannels)
                contextChannelIds.Add(ch.Id);

            var isExpanded = _expandedContexts.GetValueOrDefault(ctx.Id, true);
            AddContextGroup(ctx, ctxChannels, isExpanded);
        }

        // Ungrouped channels (no context)
        var ungrouped = channels
            .Where(ch => ch.ContextId is null && !contextChannelIds.Contains(ch.Id))
            .OrderByDescending(ch => ch.CreatedAt)
            .ToList();

        if (ungrouped.Count > 0)
        {
            AddSectionLabel("Ungrouped");
            foreach (var ch in ungrouped)
                AddChannelButton(ch);
        }
    }

    private void AddContextGroup(ContextDto ctx, List<ChannelDto> channels, bool expanded)
    {
        var header = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            Tag = ctx.Id,
        };

        var arrow = expanded ? "▾" : "▸";
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerPanel.Children.Add(new TextBlock
        {
            Text = arrow,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            Foreground = new SolidColorBrush(ColorFrom(0x00FF00)),
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = ctx.Name,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorFrom(0xCCCCCC)),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"({channels.Count})",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorFrom(0x777777)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        header.Content = headerPanel;

        var channelContainer = new StackPanel
        {
            Spacing = 0,
            Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(12, 0, 0, 0),
        };

        header.Click += (_, _) =>
        {
            var nowExpanded = channelContainer.Visibility == Visibility.Collapsed;
            channelContainer.Visibility = nowExpanded ? Visibility.Visible : Visibility.Collapsed;
            _expandedContexts[ctx.Id] = nowExpanded;
            ((headerPanel.Children[0] as TextBlock)!).Text = nowExpanded ? "▾" : "▸";
        };

        SidebarPanel.Children.Add(header);

        foreach (var ch in channels)
            AddChannelButton(ch, channelContainer);

        SidebarPanel.Children.Add(channelContainer);
    }

    private void AddSectionLabel(string text)
    {
        SidebarPanel.Children.Add(new TextBlock
        {
            Text = $"── {text} ──",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorFrom(0x777777)),
            Margin = new Thickness(8, 10, 0, 4),
        });
    }

    private void AddChannelButton(ChannelDto ch, StackPanel? parent = null)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5),
            Tag = ch.Id,
        };

        var isSelected = ch.Id == _selectedChannelId;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "#",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorFrom(isSelected ? 0x00FF00 : 0x555555)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Truncate(ch.Title, 28),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorFrom(isSelected ? 0xE0E0E0 : 0x999999)),
        });
        btn.Content = panel;

        btn.PointerEntered += (_, _) => Cursor.SetCommand($"sharpclaw channel select {ch.Id} ");
        btn.PointerExited += (_, _) => UpdateCursor();
        btn.Click += async (_, _) => await SelectChannelAsync(ch.Id, ch.Title, ch.AgentName);

        (parent ?? SidebarPanel).Children.Add(btn);
    }

    private async Task SelectChannelAsync(Guid id, string title, string? agentName)
    {
        _selectedChannelId = id;
        _selectedThreadId = null;
        _pendingNewThread = false;
        ChatTitleBlock.Text = $"# {title}";
        ChatAgentBlock.Text = agentName is not null ? $"agent: {agentName}" : string.Empty;
        UpdateCursor();

        // Fetch full channel details for allowed agents
        Guid? channelAgentId = null;
        List<Guid>? allowedAgentIds = null;
        var api = App.Services!.GetRequiredService<SharpClawApiClient>();
        try
        {
            var chResp = await api.GetAsync($"/channels/{id}");
            if (chResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await chResp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("agentId", out var aProp) && aProp.ValueKind != JsonValueKind.Null)
                    channelAgentId = aProp.GetGuid();
                if (doc.RootElement.TryGetProperty("allowedAgentIds", out var aaProp) && aaProp.ValueKind == JsonValueKind.Array)
                    allowedAgentIds = aaProp.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetGuid()).ToList();
            }
        }
        catch { /* swallow */ }

        _selectedAgentId = channelAgentId;
        await LoadSidebarAsync();
        await LoadAgentsAsync(channelAgentId, allowedAgentIds);
        await LoadThreadsAsync(id);
        await LoadHistoryAsync(id);

        DispatcherQueue.TryEnqueue(() => MessageInput.Focus(FocusState.Programmatic));
    }

    // ── Agents ─────────────────────────────────────────────────

    private async Task LoadAgentsAsync(Guid? channelAgentId, IReadOnlyList<Guid>? allowedAgentIds)
    {
        AgentSelector.SelectionChanged -= OnAgentSelectionChanged;
        AgentSelector.Items.Clear();

        var api = App.Services!.GetRequiredService<SharpClawApiClient>();

        try
        {
            var resp = await api.GetAsync("/agents");
            if (resp.IsSuccessStatusCode)
                _allAgents = JsonSerializer.Deserialize<List<AgentDto>>(
                    await resp.Content.ReadAsStringAsync(), Json) ?? [];
        }
        catch { _allAgents = []; }

        List<AgentDto> visible;
        if (_selectedChannelId is not null && channelAgentId is not null)
        {
            // Inside a channel: show only default + allowed agents
            var allowed = new HashSet<Guid> { channelAgentId.Value };
            if (allowedAgentIds is not null)
                foreach (var id in allowedAgentIds)
                    allowed.Add(id);
            visible = _allAgents.Where(a => allowed.Contains(a.Id)).ToList();
        }
        else
        {
            // No channel selected: show all agents
            visible = _allAgents;
        }

        foreach (var agent in visible)
        {
            var item = new ComboBoxItem
            {
                Content = $"{agent.Name}  ({agent.ProviderName}/{agent.ModelName})",
                Tag = agent.Id,
            };
            AgentSelector.Items.Add(item);
        }

        // Restore or default selection
        var selectedIndex = -1;
        var targetId = _selectedAgentId ?? channelAgentId;
        if (targetId is { } tgt)
        {
            for (var i = 0; i < AgentSelector.Items.Count; i++)
            {
                if (AgentSelector.Items[i] is ComboBoxItem ci && ci.Tag is Guid g && g == tgt)
                { selectedIndex = i; break; }
            }
        }
        AgentSelector.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (AgentSelector.Items.Count > 0 ? 0 : -1);

        if (AgentSelector.SelectedItem is ComboBoxItem sel && sel.Tag is Guid selId)
            _selectedAgentId = selId;
        else
            _selectedAgentId = null;

        AgentSelector.SelectionChanged += OnAgentSelectionChanged;
    }

    private void OnAgentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgentSelector.SelectedItem is ComboBoxItem { Tag: Guid agentId })
            _selectedAgentId = agentId;
        else
            _selectedAgentId = null;

        // Update the agent subtitle
        var agent = _allAgents.FirstOrDefault(a => a.Id == _selectedAgentId);
        ChatAgentBlock.Text = agent is not null ? $"agent: {agent.Name}" : string.Empty;

        UpdateCursor();
    }

    // ── Threads ──────────────────────────────────────────────────

    private async Task LoadThreadsAsync(Guid channelId)
    {
        ThreadSelectorPanel.Visibility = Visibility.Visible;
        _suppressThreadSelection = true;
        ThreadSelector.Items.Clear();

        ThreadSelector.Items.Add(new ComboBoxItem { Content = "One-off messages", Tag = null });

        var api = App.Services!.GetRequiredService<SharpClawApiClient>();
        try
        {
            var resp = await api.GetAsync($"/channels/{channelId}/threads");
            if (resp.IsSuccessStatusCode)
            {
                var threads = JsonSerializer.Deserialize<List<ThreadDto>>(
                    await resp.Content.ReadAsStringAsync(), Json);
                if (threads is not null)
                {
                    foreach (var t in threads)
                    {
                        var item = new ComboBoxItem { Content = t.Name, Tag = t.Id };
                        item.PointerEntered += (_, _) => Cursor.SetCommand($"sharpclaw thread select {t.Id} ");
                        item.PointerExited += (_, _) => UpdateCursor();
                        ThreadSelector.Items.Add(item);
                    }
                }
            }
        }
        catch { /* API not reachable */ }

        var selectedIndex = 0;
        if (_selectedThreadId is { } tid)
        {
            for (var i = 0; i < ThreadSelector.Items.Count; i++)
            {
                if (ThreadSelector.Items[i] is ComboBoxItem ci && ci.Tag is Guid g && g == tid)
                { selectedIndex = i; break; }
            }
        }

        ThreadSelector.SelectedIndex = selectedIndex;
        _suppressThreadSelection = false;

        OneOffWarning.Visibility = _selectedThreadId is null && !_pendingNewThread
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnThreadSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThreadSelection) return;

        // If user picks a real item, leave pending-new-thread mode and remove placeholder
        if (_pendingNewThread)
        {
            _pendingNewThread = false;
            RemovePendingThreadPlaceholder();
        }

        if (ThreadSelector.SelectedItem is ComboBoxItem { Tag: Guid tid })
            _selectedThreadId = tid;
        else
            _selectedThreadId = null;

        OneOffWarning.Visibility = _selectedThreadId is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_selectedChannelId is { } channelId)
        {
            await LoadHistoryAsync(channelId);
            UpdateCursor();
        }
    }

    private void RemovePendingThreadPlaceholder()
    {
        for (var i = ThreadSelector.Items.Count - 1; i >= 0; i--)
        {
            if (ThreadSelector.Items[i] is ComboBoxItem { Tag: string tag } && tag == "__pending__")
            {
                _suppressThreadSelection = true;
                ThreadSelector.Items.RemoveAt(i);
                _suppressThreadSelection = false;
                break;
            }
        }
    }

    private void OnAddThreadClick(object sender, RoutedEventArgs e)
    {
        if (_selectedChannelId is null) return;

        _selectedThreadId = null;
        _pendingNewThread = true;
        MessagesPanel.Children.Clear();
        OneOffWarning.Visibility = Visibility.Collapsed;

        // Remove any existing pending placeholder, then add a new one and select it
        RemovePendingThreadPlaceholder();
        _suppressThreadSelection = true;
        var placeholder = new ComboBoxItem
        {
            Content = "[Send a message to create new thread]",
            Tag = "__pending__",
            FontStyle = Windows.UI.Text.FontStyle.Italic,
        };
        ThreadSelector.Items.Add(placeholder);
        ThreadSelector.SelectedItem = placeholder;
        _suppressThreadSelection = false;

        UpdateCursor();
        DispatcherQueue.TryEnqueue(() => MessageInput.Focus(FocusState.Programmatic));
    }

    // ── Messages ─────────────────────────────────────────────────

    private async Task LoadHistoryAsync(Guid channelId)
    {
        MessagesPanel.Children.Clear();
        var api = App.Services!.GetRequiredService<SharpClawApiClient>();

        try
        {
            var url = _selectedThreadId is { } tid
                ? $"/channels/{channelId}/chat/threads/{tid}"
                : $"/channels/{channelId}/chat";
            var resp = await api.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;

            var messages = JsonSerializer.Deserialize<List<ChatMessageDto>>(
                await resp.Content.ReadAsStringAsync(), Json);

            if (messages is null) return;

            foreach (var msg in messages)
                AppendMessage(msg.Role, msg.Content, msg.Timestamp);
        }
        catch { /* swallow */ }

        ScrollToBottom();
    }

    private void AppendMessage(string role, string content, DateTimeOffset? timestamp)
    {
        var isUser = role.Equals("user", StringComparison.OrdinalIgnoreCase);

        var bubble = new Border
        {
            Background = new SolidColorBrush(ColorFrom(isUser ? 0x1A2A1A : 0x1A1A1A)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 600,
            Margin = new Thickness(0, 2, 0, 2),
        };

        var stack = new StackPanel { Spacing = 4 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = isUser ? "you" : "assistant",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorFrom(isUser ? 0x00FF00 : 0x00AAFF)),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        });
        if (timestamp.HasValue)
        {
            header.Children.Add(new TextBlock
            {
                Text = timestamp.Value.LocalDateTime.ToString("HH:mm"),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 10,
                Foreground = new SolidColorBrush(ColorFrom(0x444444)),
            });
        }
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = content,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorFrom(0xCCCCCC)),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        bubble.Child = stack;
        MessagesPanel.Children.Add(bubble);
    }

    private void ScrollToBottom()
    {
        MessagesScroller.UpdateLayout();
        MessagesScroller.ChangeView(null, MessagesScroller.ScrollableHeight, null);
    }

    // ── Send ─────────────────────────────────────────────────────

    private void OnMessageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(MessageInput.Text))
        {
            e.Handled = true;
            _ = SendMessageAsync();
        }
    }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(MessageInput.Text))
            _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        var text = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        MessageInput.Text = string.Empty;
        var api = App.Services!.GetRequiredService<SharpClawApiClient>();

        // Auto-create channel + thread when none is selected
        if (_selectedChannelId is null)
        {
            try
            {
                var title = Truncate(text, 10);
                var chBody = JsonSerializer.Serialize(new { title, agentId = _selectedAgentId }, Json);
                var chContent = new StringContent(chBody, Encoding.UTF8, "application/json");
                var chResp = await api.PostAsync("/channels", chContent);
                if (!chResp.IsSuccessStatusCode)
                {
                    AppendMessage("assistant",
                        $"✗ Failed to create channel: {(int)chResp.StatusCode} {chResp.ReasonPhrase}",
                        DateTimeOffset.Now);
                    return;
                }

                using var chDoc = JsonDocument.Parse(await chResp.Content.ReadAsStringAsync());
                var chId = chDoc.RootElement.GetProperty("id").GetGuid();
                var chTitle = chDoc.RootElement.GetProperty("title").GetString() ?? title;
                var agentName = chDoc.RootElement.TryGetProperty("agentName", out var anProp)
                    ? anProp.GetString() : null;

                var thBody = JsonSerializer.Serialize(new { name = "Default" }, Json);
                var thContent = new StringContent(thBody, Encoding.UTF8, "application/json");
                var thResp = await api.PostAsync($"/channels/{chId}/threads", thContent);
                if (thResp.IsSuccessStatusCode)
                {
                    using var thDoc = JsonDocument.Parse(await thResp.Content.ReadAsStringAsync());
                    _selectedThreadId = thDoc.RootElement.GetProperty("id").GetGuid();
                }

                _selectedChannelId = chId;
                ChatTitleBlock.Text = $"# {chTitle}";
                ChatAgentBlock.Text = agentName is not null ? $"agent: {agentName}" : string.Empty;
                await LoadSidebarAsync();
                await LoadAgentsAsync(_selectedAgentId, null);
                await LoadThreadsAsync(chId);
                UpdateCursor();
            }
            catch (Exception ex)
            {
                AppendMessage("assistant", $"✗ {ex.Message}", DateTimeOffset.Now);
                return;
            }
        }

        // Auto-create thread when the "+" button was pressed
        if (_pendingNewThread && _selectedChannelId is { } pendingChId)
        {
            _pendingNewThread = false;
            try
            {
                var threadName = Truncate(text, 10);
                var thBody = JsonSerializer.Serialize(new { name = threadName }, Json);
                var thContent = new StringContent(thBody, Encoding.UTF8, "application/json");
                var thResp = await api.PostAsync($"/channels/{pendingChId}/threads", thContent);
                if (thResp.IsSuccessStatusCode)
                {
                    var thread = JsonSerializer.Deserialize<ThreadDto>(
                        await thResp.Content.ReadAsStringAsync(), Json);
                    if (thread is not null)
                    {
                        _selectedThreadId = thread.Id;

                        // Replace pending placeholder with the real thread item
                        _suppressThreadSelection = true;
                        RemovePendingThreadPlaceholder();
                        var newItem = new ComboBoxItem { Content = thread.Name, Tag = thread.Id };
                        newItem.PointerEntered += (_, _) => Cursor.SetCommand($"sharpclaw thread select {thread.Id} ");
                        newItem.PointerExited += (_, _) => UpdateCursor();
                        ThreadSelector.Items.Add(newItem);
                        ThreadSelector.SelectedItem = newItem;
                        OneOffWarning.Visibility = Visibility.Collapsed;
                        _suppressThreadSelection = false;
                    }
                }
                else
                {
                    AppendMessage("assistant",
                        $"✗ Failed to create thread: {(int)thResp.StatusCode} {thResp.ReasonPhrase}",
                        DateTimeOffset.Now);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendMessage("assistant", $"✗ Failed to create thread: {ex.Message}", DateTimeOffset.Now);
                return;
            }
        }

        var channelId = _selectedChannelId!.Value;

        AppendMessage("user", text, DateTimeOffset.Now);
        ScrollToBottom();

        UpdateCursor(text);

        var assistantContent = new TextBlock
        {
            Text = "▍",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorFrom(0xCCCCCC)),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        AppendStreamingBubble(assistantContent);
        ScrollToBottom();

        var accumulated = new StringBuilder();
        var dispatcher = DispatcherQueue;

        try
        {
            var body = JsonSerializer.Serialize(new { message = text, agentId = _selectedAgentId, clientType = 1 }, Json);
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var streamUrl = _selectedThreadId is { } tid
                ? $"/channels/{channelId}/chat/threads/{tid}/stream"
                : $"/channels/{channelId}/chat/stream";

            using var resp = await api.PostStreamAsync(streamUrl, content);

            if (!resp.IsSuccessStatusCode)
            {
                assistantContent.Text = $"✗ Error {(int)resp.StatusCode}: {resp.ReasonPhrase}";
                assistantContent.Foreground = new SolidColorBrush(ColorFrom(0xFF4444));
                return;
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            string? eventType = null;

            await Task.Run(async () =>
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null) break;

                    if (line.StartsWith("event: "))
                    {
                        eventType = line[7..];
                    }
                    else if (line.StartsWith("data: ") && eventType is not null)
                    {
                        var payload = line[6..];

                        switch (eventType)
                        {
                            case "TextDelta":
                                using (var doc = JsonDocument.Parse(payload))
                                {
                                    if (doc.RootElement.TryGetProperty("delta", out var dp)
                                        && dp.GetString() is { } delta)
                                    {
                                        accumulated.Append(delta);
                                        var snapshot = accumulated.ToString();
                                        dispatcher.TryEnqueue(() =>
                                        {
                                            assistantContent.Text = snapshot + "▍";
                                            ScrollToBottom();
                                        });
                                    }
                                }
                                break;

                            case "Error":
                                using (var doc = JsonDocument.Parse(payload))
                                {
                                    if (doc.RootElement.TryGetProperty("error", out var ep))
                                    {
                                        var errorMsg = ep.GetString();
                                        dispatcher.TryEnqueue(() =>
                                        {
                                            assistantContent.Text = $"✗ {errorMsg}";
                                            assistantContent.Foreground =
                                                new SolidColorBrush(ColorFrom(0xFF4444));
                                        });
                                    }
                                }
                                break;

                            case "Done":
                                var finalText = accumulated.Length > 0
                                    ? accumulated.ToString()
                                    : "(empty response)";
                                dispatcher.TryEnqueue(() =>
                                {
                                    assistantContent.Text = finalText;
                                });
                                break;
                        }

                        eventType = null;
                    }
                }
            });

            // Clean up trailing cursor if stream ended without a Done event
            if (assistantContent.Text.EndsWith("▍"))
                assistantContent.Text = accumulated.Length > 0
                    ? accumulated.ToString()
                    : "(empty response)";
        }
        catch (Exception ex)
        {
            assistantContent.Text = $"✗ {ex.Message}";
            assistantContent.Foreground = new SolidColorBrush(ColorFrom(0xFF4444));
        }
        finally
        {
            ScrollToBottom();
            UpdateCursor();
            DispatcherQueue.TryEnqueue(() => MessageInput.Focus(FocusState.Programmatic));
        }
    }

    private void AppendStreamingBubble(TextBlock contentBlock)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(ColorFrom(0x1A1A1A)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 600,
            Margin = new Thickness(0, 2, 0, 2),
        };

        var stack = new StackPanel { Spacing = 4 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = "assistant",
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorFrom(0x00AAFF)),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        });
        header.Children.Add(new TextBlock
        {
            Text = DateTimeOffset.Now.LocalDateTime.ToString("HH:mm"),
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorFrom(0x444444)),
        });
        stack.Children.Add(header);
        stack.Children.Add(contentBlock);

        bubble.Child = stack;
        MessagesPanel.Children.Add(bubble);
    }

    // ── Navigation ───────────────────────────────────────────────

    private async void OnNewChannelClick(object sender, RoutedEventArgs e)
    {
        _selectedChannelId = null;
        _selectedThreadId = null;
        _selectedAgentId = null;
        _pendingNewThread = false;
        ChatTitleBlock.Text = "> Select or create a channel";
        ChatAgentBlock.Text = string.Empty;
        ThreadSelectorPanel.Visibility = Visibility.Collapsed;
        OneOffWarning.Visibility = Visibility.Collapsed;
        MessagesPanel.Children.Clear();

        await LoadSidebarAsync();
        await LoadAgentsAsync(null, null);
        UpdateCursor();
    }

    private void OnNewChannelPointerEntered(object sender, PointerRoutedEventArgs e)
        => Cursor.SetCommand("sharpclaw channel new ");

    private void OnNewChannelPointerExited(object sender, PointerRoutedEventArgs e)
        => UpdateCursor();

    private void OnDashboardClick(object sender, RoutedEventArgs e)
    {
        if (App.Services is not { } services) return;
        var navigator = services.GetRequiredService<INavigator>();
        _ = navigator.NavigateRouteAsync(this, "Dashboard");
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        if (App.Services is not { } services) return;
        var api = services.GetRequiredService<SharpClawApiClient>();
        api.SetAccessToken(null!);
        var navigator = services.GetRequiredService<INavigator>();
        _ = navigator.NavigateRouteAsync(this, "Login", qualifier: Qualifiers.ClearBackStack);
    }

    private async void OnMatrixCommunityClick(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://matrix.to/#/#p1:matrix.mkn8rn.com"));

    private async void OnCreatorBlogClick(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://blog.mkn8rn.com"));

    // ── Helpers ──────────────────────────────────────────────────

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private void OnMessageTextChanged(object sender, TextChangedEventArgs e) => UpdateCursor();

    private void UpdateCursor(string? overrideMessage = null)
    {
        var channelPart = _selectedChannelId is { } id ? id.ToString() : "new-channel";
        var msg = overrideMessage ?? MessageInput.Text ?? string.Empty;
        var cmd = $"sharpclaw chat {channelPart}";
        if (_selectedThreadId is { } tid)
            cmd += $" --thread {tid}";
        if (msg.Length > 0)
            cmd += " " + Truncate(msg.Trim(), 40);
        Cursor.SetCommand(cmd + " ");
    }

    private static Windows.UI.Color ColorFrom(int rgb)
        => Windows.UI.Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    // ── DTOs for JSON deserialization ────────────────────────────

    [ImplicitKeys(IsEnabled = false)]
    private sealed partial record ContextDto(Guid Id, string Name, Guid AgentId, string AgentName, DateTimeOffset CreatedAt);
    [ImplicitKeys(IsEnabled = false)]
    private sealed partial record ChannelDto(Guid Id, string Title, Guid? AgentId, string? AgentName, Guid? ContextId, DateTimeOffset CreatedAt);
    private sealed record ChatMessageDto(string Role, string Content, DateTimeOffset Timestamp);
    private sealed record ChatResponseDto(ChatMessageDto UserMessage, ChatMessageDto AssistantMessage);
    [ImplicitKeys(IsEnabled = false)]
    private sealed partial record ThreadDto(Guid Id, string Name, Guid ChannelId, int? MaxMessages, int? MaxCharacters, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    [ImplicitKeys(IsEnabled = false)]
    private sealed partial record AgentDto(Guid Id, string Name, string? SystemPrompt, Guid ModelId, string ModelName, string ProviderName, Guid? RoleId = null, string? RoleName = null);
}
