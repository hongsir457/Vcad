using System.Text;

namespace Vcad.AgentLite.Providers;

internal static class AttachmentPromptBuilder
{
    private const int MaxAttachmentTextChars = 16000;
    private const int MaxCadStateChars = 32000;
    private const int MaxPromptChars = 48000;

    public static string BuildUserPrompt(ParseRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine((req.text ?? "").Trim());
        var attachments = req.attachments ?? new List<ParseAttachment>();

        if (attachments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Attachment context:");
            foreach (var attachment in attachments)
            {
                AppendAttachment(sb, attachment);
            }
        }

        if (req.cad_state != null)
        {
            sb.AppendLine();
            sb.AppendLine("Current DWG memory snapshot (read-only CAD state, including expanded block entities when available):");
            var cadState = req.cad_state.ToJsonString();
            if (cadState.Length > MaxCadStateChars)
            {
                cadState = cadState.Substring(0, MaxCadStateChars) + "\n...[cad_state truncated]";
            }
            sb.AppendLine(cadState);
        }

        var prompt = sb.ToString().Trim();
        if (prompt.Length <= MaxPromptChars) return prompt;
        return prompt.Substring(0, MaxPromptChars) + "\n...[attachment context truncated]";
    }

    public static object BuildOpenAiUserContent(ParseRequest req, bool includeImages)
    {
        var images = includeImages ? InlineImages(req).ToList() : new List<ParseAttachment>();
        if (images.Count == 0) return BuildUserPrompt(req);

        var parts = new List<object>
        {
            new { type = "text", text = BuildUserPrompt(req) },
        };
        foreach (var image in images)
        {
            parts.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = "data:" + image.mime_type + ";base64," + image.data_base64,
                },
            });
        }
        return parts.ToArray();
    }

    public static object BuildAnthropicUserContent(ParseRequest req, bool includeImages)
    {
        var images = includeImages ? InlineImages(req).ToList() : new List<ParseAttachment>();
        if (images.Count == 0) return BuildUserPrompt(req);

        var parts = new List<object>
        {
            new { type = "text", text = BuildUserPrompt(req) },
        };
        foreach (var image in images)
        {
            parts.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = image.mime_type,
                    data = image.data_base64,
                },
            });
        }
        return parts.ToArray();
    }

    public static object[] BuildGeminiParts(ParseRequest req, bool includeImages)
    {
        var parts = new List<object>
        {
            new { text = BuildUserPrompt(req) },
        };
        if (includeImages)
        {
            foreach (var image in InlineImages(req))
            {
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = image.mime_type,
                        data = image.data_base64,
                    },
                });
            }
        }
        return parts.ToArray();
    }

    public static string[] ImageBase64Payloads(ParseRequest req)
    {
        return InlineImages(req)
            .Select(image => image.data_base64!)
            .ToArray();
    }

    private static void AppendAttachment(StringBuilder sb, ParseAttachment attachment)
    {
        sb.Append("- ")
            .Append(string.IsNullOrWhiteSpace(attachment.id) ? "attachment" : attachment.id)
            .Append(": name=").Append(attachment.name)
            .Append(", kind=").Append(attachment.kind)
            .Append(", mime=").Append(attachment.mime_type)
            .Append(", bytes=").Append(attachment.size_bytes);
        if (!string.IsNullOrWhiteSpace(attachment.sha256))
        {
            sb.Append(", sha256=").Append(attachment.sha256);
        }
        if (attachment.page_count.HasValue)
        {
            sb.Append(", pages=").Append(attachment.page_count.Value);
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(attachment.note))
        {
            sb.AppendLine("  note: " + attachment.note);
        }
        if (!string.IsNullOrWhiteSpace(attachment.text_excerpt))
        {
            var text = attachment.text_excerpt!;
            if (text.Length > MaxAttachmentTextChars)
            {
                text = text.Substring(0, MaxAttachmentTextChars) + "\n...[text excerpt truncated]";
            }
            sb.AppendLine("  text_excerpt:");
            sb.AppendLine(text);
        }
        else if (IsImage(attachment) && !string.IsNullOrWhiteSpace(attachment.data_base64))
        {
            sb.AppendLine("  image_payload: attached for vision-capable providers.");
        }
    }

    private static IEnumerable<ParseAttachment> InlineImages(ParseRequest req)
    {
        return (req.attachments ?? Enumerable.Empty<ParseAttachment>()).Where(attachment =>
            IsImage(attachment) &&
            !string.IsNullOrWhiteSpace(attachment.data_base64) &&
            !string.IsNullOrWhiteSpace(attachment.mime_type));
    }

    private static bool IsImage(ParseAttachment attachment)
    {
        return string.Equals(attachment.kind, "image", StringComparison.OrdinalIgnoreCase) ||
            attachment.mime_type.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
