using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RZDTrainer.Models;
using System.Windows.Media;

namespace RZDTrainer.Views
{
    public partial class ScenarioSelectorWindow : Window
    {
        private List<Scenario> _allScenarios;
        private List<Scenario> _filteredScenarios;
        private int _selectedIndex = -1;
        
        public int SelectedScenarioIndex { get; private set; } = -1;
        
        public ScenarioSelectorWindow(List<Scenario> scenarios, int currentIndex)
        {
            InitializeComponent();
            
            _allScenarios = scenarios;
            _filteredScenarios = new List<Scenario>(scenarios);
            _selectedIndex = currentIndex;
            
            ScenariosListBox.ItemsSource = _filteredScenarios;
            
            if (_selectedIndex >= 0 && _selectedIndex < _filteredScenarios.Count)
            {
                ScenariosListBox.SelectedIndex = _selectedIndex;
            }
            
            UpdateSelectedIndicator();
        }
        
        private void UpdateSelectedIndicator()
        {
            // Обновляем видимость индикаторов после загрузки
            ScenariosListBox.Loaded += (s, e) =>
            {
                for (int i = 0; i < ScenariosListBox.Items.Count; i++)
                {
                    var container = ScenariosListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (container != null)
                    {
                        var indicator = FindVisualChild<TextBlock>(container, "SelectedIndicator");
                        if (indicator != null)
                        {
                            var scenario = _filteredScenarios[i];
                            indicator.Visibility = _allScenarios.IndexOf(scenario) == _selectedIndex 
                                ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
            };
        }
        
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
        
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchBox.Text?.ToLower() ?? "";
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _filteredScenarios = new List<Scenario>(_allScenarios);
            }
            else
            {
                _filteredScenarios = _allScenarios
                    .Where(s => s.Id.ToLower().Contains(searchText) ||
                                s.Title.ToLower().Contains(searchText) ||
                                s.Context.ToLower().Contains(searchText))
                    .ToList();
            }
            
            ScenariosListBox.ItemsSource = null;
            ScenariosListBox.ItemsSource = _filteredScenarios;
        }
        
        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScenariosListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Выберите сценарий", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var selectedScenario = _filteredScenarios[ScenariosListBox.SelectedIndex];
            SelectedScenarioIndex = _allScenarios.IndexOf(selectedScenario);
            
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}