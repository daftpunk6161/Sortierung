function Start-WpfApp {
  <# Structured WPF app entry compatible with componentized wpf/ layout. #>
  param([switch]$Headless)

  return (Start-WpfGui -Headless:$Headless)
}
