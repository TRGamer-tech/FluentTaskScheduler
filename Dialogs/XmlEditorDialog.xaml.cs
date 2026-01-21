using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs
{
    public sealed partial class XmlEditorDialog : ContentDialog
    {
        public string XmlContent 
        { 
            get => XmlTextBox.Text; 
            set => XmlTextBox.Text = value; 
        }

        public XmlEditorDialog(string xml)
        {
            this.InitializeComponent();
            this.XmlContent = xml;
        }
    }
}
