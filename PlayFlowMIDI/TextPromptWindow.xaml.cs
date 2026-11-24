using System.Windows;
using System.Windows.Controls;

namespace PlayFlowMIDI
{
    /// <summary>Small "type a name" dialog. The validator returns null to accept or the message to show.</summary>
    public partial class TextPromptWindow : Window
    {
        private readonly Func<string, string?> _validate;
        private bool _edited;

        /// <summary>The accepted text, trimmed.</summary>
        public string Value { get; private set; } = string.Empty;

        public TextPromptWindow(string title, string prompt, string initialValue, Func<string, string?> validate)
        {
            InitializeComponent();

            _validate = validate;
            Title = title;
            PromptText.Text = prompt;
            ValueBox.Text = initialValue;
            ValueBox.SelectAll();
            _edited = false;
            OkButton.IsEnabled = _validate(initialValue.Trim()) == null;

            Loaded += (_, _) => ValueBox.Focus();
        }

        private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (OkButton == null) return;

            _edited = true;
            string? error = _validate(ValueBox.Text.Trim());
            OkButton.IsEnabled = error == null;
            // Do not complain about the empty box before the user has typed anything.
            ErrorText.Text = _edited && ValueBox.Text.Length > 0 ? error ?? string.Empty : string.Empty;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string value = ValueBox.Text.Trim();
            if (_validate(value) != null) return;

            Value = value;
            DialogResult = true;
        }
    }
}
