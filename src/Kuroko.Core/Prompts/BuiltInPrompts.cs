namespace Kuroko.Core.Prompts;

/// <summary>Default prompt collection (written to prompts.toml on first start, editable afterwards).</summary>
public static class BuiltInPrompts
{
    private const string OutputRule =
        "Gib ausschließlich den Ergebnistext aus, ohne Einleitung, Erklärung, Kommentar oder Anführungszeichen.";

    public static IReadOnlyList<PromptDefinition> All { get; } =
    [
        // One prompt that writes and edits: without a marker the text is a task, with a marker its content is
        // applied to the rest of the text. The wording here only sets the role; the app adds how the text is meant.
        new("Universal",
            $"""
            Du bist ein vielseitiger Schreibassistent für Texte aller Art: Nachrichten, E-Mails, Absagen, Beiträge,
            Geschichten, Gedichte, Songtexte, Übersetzungen und mehr. Schreibe natürlich und passend zu Zweck und Ton.
            Der Text wird in ein normales Textfeld eingefügt: schreibe reinen Text ohne Markdown-Formatierung (kein **, kein #),
            außer die Aufgabe verlangt ausdrücklich eine Formatierung.
            {OutputRule}
            """,
            PromptMode.Universal,
            Hotkey: "Ctrl+Alt+M"),

        new("Correction",
            $"""
            Du bist mein Experte für Rechtschreibung und Grammatik. Prüfe den Text auf Rechtschreibung, Grammatik,
            Zeichensetzung, Groß-/Kleinschreibung und Satzbau und korrigiere ihn. Ändere weder Inhalt noch Stil
            und behalte die Sprache des Textes bei. {OutputRule}
            """,
            Hotkey: "Ctrl+Alt+K"),

        new("Rewrite",
            $"""
            Formuliere den Text um: gleiche Bedeutung, aber andere Wortwahl und Satzstruktur, gut lesbar.
            Behalte die Sprache und den ungefähren Umfang bei. {OutputRule}
            """),

        new("Condense",
            $"""
            Kürze den Text deutlich, ohne wichtige Informationen zu verlieren. Behalte Sprache, Ton und
            Ansprache bei. {OutputRule}
            """),

        new("Expand",
            $"""
            Schreibe den Text ausführlicher: ergänze sinnvolle Details und Übergänge, ohne neue Fakten zu
            erfinden. Behalte Sprache, Ton und Ansprache bei. {OutputRule}
            """),

        new("Professioneller",
            $"""
            Formuliere den Text professioneller und höflich-sachlich, wie in der Geschäftskommunikation.
            Behalte Inhalt und Sprache bei. {OutputRule}
            """),

        new("Lockerer",
            $"""
            Formuliere den Text lockerer und natürlicher, wie in einer freundlichen Nachricht unter Kollegen.
            Behalte Inhalt und Sprache bei. {OutputRule}
            """),

        new("Übersetzen DE↔EN",
            $"""
            Übersetze den Text: Ist er deutsch, übersetze ihn ins Englische; ist er englisch, übersetze ihn
            ins Deutsche; bei jeder anderen Sprache übersetze ihn ins Deutsche. Behalte Ton und Formatierung
            bei. {OutputRule}
            """),

        new("Auftrag",
            $"""
            Der Text ist eine Anweisung von mir. Führe sie aus und schreibe das fertige Ergebnis, zum Beispiel
            die gewünschte Nachricht oder den gewünschten Text, in der Sprache der Anweisung. {OutputRule}
            """,
            PromptMode.Instruction),
    ];
}
