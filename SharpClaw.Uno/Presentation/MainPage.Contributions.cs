using System.Text;
using System.Text.Json;
using SharpClaw.Contracts.Modules;
using SharpClaw.Helpers;
using SharpClaw.Services;

namespace SharpClaw.Presentation;

public sealed partial class MainPage
{
    private void RebuildChatActionContributions()
    {
        if (App.Services is null) return;

        var registry = App.Services.GetService<ModuleFrontendContributionRegistry>();
        if (registry is null) return;

        ChatActionsPanel.Children.Clear();
        foreach (var contribution in registry.GetActive(FrontendContributionPoint.ChatInputAction))
        {
            var button = BuildChatActionButton(contribution);
            if (button is not null)
                ChatActionsPanel.Children.Add(button);
        }

        ChatActionsPanel.Children.Add(MicButton);
        UpdateMicState();
    }

    private Button? BuildChatActionButton(ModuleFrontendContribution contribution)
    {
        if (contribution.Action is null
            || !contribution.BuilderKey.Equals("api-button", StringComparison.OrdinalIgnoreCase))
            return null;

        var glyph = string.IsNullOrWhiteSpace(contribution.Icon) ? "+" : contribution.Icon!;
        var button = new Button
        {
            Background = TerminalUI.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            MinWidth = 0,
            MinHeight = 0,
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = TerminalUI.Mono,
                FontSize = 14,
                Foreground = TerminalUI.Brush(0x00FF00),
            },
        };

        ToolTipService.SetToolTip(button, string.IsNullOrWhiteSpace(contribution.Tooltip)
            ? contribution.Label
            : contribution.Tooltip);
        button.Click += async (_, _) => await InvokeChatActionContributionAsync(contribution);
        return button;
    }

    private async Task InvokeChatActionContributionAsync(ModuleFrontendContribution contribution)
    {
        if (contribution.Action is null || App.Services is null) return;

        var api = App.Services.GetRequiredService<SharpClawApiClient>();
        var method = contribution.Action.Method.Trim().ToUpperInvariant();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var resp = method switch
        {
            "GET" => await api.GetAsync(contribution.Action.InternalApiPath),
            "POST" => await api.PostAsync(contribution.Action.InternalApiPath, content),
            _ => null,
        };

        if (resp is null || !resp.IsSuccessStatusCode) return;
        if (!string.Equals(contribution.Action.ResponseMode, "insert-text", StringComparison.OrdinalIgnoreCase))
            return;

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (!doc.RootElement.TryGetProperty("text", out var textValue)
            || textValue.ValueKind != JsonValueKind.String)
            return;

        var text = textValue.GetString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var existing = MessageInput.Text ?? string.Empty;
        MessageInput.Text = string.IsNullOrWhiteSpace(existing)
            ? text
            : $"{existing} {text}";
        MessageInput.Focus(FocusState.Programmatic);
    }
}
