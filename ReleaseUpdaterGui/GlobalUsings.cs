// ImplicitUsings brings in a project-wide "global using System.Windows.Forms;"
// (see obj/.../ReleaseUpdaterGui.GlobalUsings.g.cs), which makes MessageBox/
// MessageBoxButtons/MessageBoxIcon ambiguous with ErikwnkWFUI.Forms' own
// same-named types wherever "using ErikwnkWFUI.Forms;" is also in scope -
// these aliases resolve that project-wide instead of patching every file.
global using MessageBox = ErikwnkWFUI.Forms.MessageBox;
global using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
global using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;
