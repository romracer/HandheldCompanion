﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Xml.Linq;
using ControllerCommon.Controllers;
using ControllerCommon.Inputs;
using ControllerCommon.Utils;
using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Controls;
using HandheldCompanion.Simulators;
using HandheldCompanion.Views;
using HandheldCompanion.Views.Classes;
using HandheldCompanion.Views.Windows;
using Inkore.UI.WPF.Modern.Controls;
using Frame = Inkore.UI.WPF.Modern.Controls.Frame;
using Page = System.Windows.Controls.Page;
using Timer = System.Timers.Timer;

namespace HandheldCompanion.Managers
{
    public static class GamepadFocusManager
    {
        #region events
        public static event GotFocusEventHandler GotFocus;
        public delegate void GotFocusEventHandler(Control control);

        public static event LostFocusEventHandler LostFocus;
        public delegate void LostFocusEventHandler(Control control);
        #endregion

        private static GamepadWindow _currentWindow;
        private static ConcurrentDictionary<object, Frame> _gamepadFrame = new();
        private static ConcurrentDictionary<object, Page> _gamepadPage = new();
        private static Timer _gamepadTimer;

        private static bool _goingBack;
        private static bool _goingForward;

        private static bool _rendered;

        private static ButtonState prevButtonState = new();

        // key: Windows, value: NavigationViewItem
        private static ConcurrentDictionary<object, Control> prevNavigation = new();
        // key: Page
        private static ConcurrentDictionary<object, Control> prevControl = new();

        static GamepadFocusManager()
        {
            var mainWindow = MainWindow.GetCurrent();
            mainWindow.ContentFrame.Navigated += ContentFrame_Navigated;
            mainWindow.Activated += GamepadFocusManager_GotFocus;
            mainWindow.Deactivated += GamepadFocusManager_LostFocus;

            MainWindow.overlayquickTools.ContentFrame.Navigated += ContentFrame_Navigated;
            MainWindow.overlayquickTools.Activated += GamepadFocusManager_GotFocus;
            MainWindow.overlayquickTools.Deactivated += GamepadFocusManager_LostFocus;

            ControllerManager.InputsUpdated += InputsUpdated;
            SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

            _gamepadTimer = new Timer(250) { AutoReset = false };
            _gamepadTimer.Elapsed += _gamepadTimer_Elapsed;
        }

        private static void GamepadFocusManager_LostFocus(object? sender, System.EventArgs e)
        {
            if (_currentWindow == (GamepadWindow)sender)
            {
                // halt timer
                _gamepadTimer.Stop();

                // raise event
                LostFocus?.Invoke(_currentWindow);

                // reset current window
                _currentWindow = null;
            }
        }

        private static void GamepadFocusManager_GotFocus(object? sender, System.EventArgs e)
        {
            // set current window
            _currentWindow = (GamepadWindow)sender;

            // raise event
            GotFocus?.Invoke(_currentWindow);
        }

        private static void SettingsManager_SettingValueChanged(string name, object value)
        {
            // UI thread (async)
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                switch (name)
                {
                    case "DesktopLayoutEnabled":
                        {
                            var value = SettingsManager.GetBoolean(name, true);
                            switch(value)
                            {
                                case true:
                                    ControllerManager.InputsUpdated -= InputsUpdated;
                                    break;
                                case false:
                                    ControllerManager.InputsUpdated += InputsUpdated;
                                    break;
                            }
                        }
                        break;
                }
            });
        }

        private static void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // set rendering state
            _rendered = false;

            // remove state
            _goingForward = false;

            // store current window
            _currentWindow = (GamepadWindow)Window.GetWindow((DependencyObject)sender);

            // store current Frame
            _gamepadFrame[_currentWindow] = (Frame)sender;
            _gamepadFrame[_currentWindow].ContentRendered += _gamepadFrame_ContentRendered;

            // store current Page
            _gamepadPage[_currentWindow] = (Page)_gamepadFrame[_currentWindow].Content;
        }

        private static void _gamepadFrame_ContentRendered(object? sender, EventArgs e)
        {
            _gamepadTimer.Stop();
            _gamepadTimer.Start();
        }

        private static void _gamepadTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // UI thread (async)
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_currentWindow is null)
                    return;

                // specific-cases
                switch (_gamepadPage[_currentWindow].Tag)
                {
                    case "layout":
                    case "SettingsMode0":
                    case "SettingsMode1":
                        _goingForward = true;
                        break;
                }

                if (_goingBack && prevControl.TryGetValue(_gamepadPage[_currentWindow].Tag, out Control control))
                {
                    Focus(control);

                    // remove state
                    _goingBack = false;
                }
                else if (_goingForward)
                {
                    if (prevControl.TryGetValue(_gamepadPage[_currentWindow].Tag, out control))
                        Focus(control);
                    else
                    {
                        control = WPFUtils.GetTopLeftControl<Control>(_currentWindow.elements);
                        Focus(control);
                    }
                }
                else if (!prevNavigation.ContainsKey(_currentWindow.Tag))
                {
                    NavigationViewItem currentNavigationViewItem = (NavigationViewItem)WPFUtils.GetTopLeftControl<NavigationViewItem>(_currentWindow.elements);
                    prevNavigation[_currentWindow.Tag] = currentNavigationViewItem;
                    Focus(currentNavigationViewItem);
                }

                // clear history
                if (_gamepadPage.ContainsKey(_currentWindow))
                    prevControl.Remove(_gamepadPage[_currentWindow].Tag, out _);

                // set rendering state
                _rendered = true;
            });
        }

        public static void Focus(Control control)
        {
            if (control is null)
                return;

            // set focus to control
            Keyboard.Focus(control);
            control.Focus();
        }

        public static Control FocusedElement(GamepadWindow window)
        {
            Control keyboardFocused = (Control)Keyboard.FocusedElement;

            if (keyboardFocused is null)
            {
                if (window is not null)
                    keyboardFocused = window;
                else if (_currentWindow is not null)
                    keyboardFocused = _currentWindow;
                else
                    return null;
            }

            string keyboardType = keyboardFocused.GetType().Name;

            switch(keyboardType)
            {
                case "MainWindow":
                case "OverlayQuickTools":
                    {
                        if (prevNavigation.ContainsKey(window.Tag))
                        {
                            // a new page opened
                            keyboardFocused = WPFUtils.GetTopLeftControl<Control>(window.elements);
                        }
                        else
                        {
                            // first start
                            keyboardFocused = WPFUtils.GetTopLeftControl<NavigationViewItem>(window.elements);
                        }
                    }
                    break;

                case "NavigationViewItem":
                    {
                        switch (keyboardFocused.Name)
                        {
                            case "b_ServiceStart":
                            case "b_ServiceStop":
                            case "b_ServiceInstall":
                            case "b_ServiceDelete":
                                break;
                            default:
                                {
                                    // update navigation
                                    prevNavigation[window.Tag] = (NavigationViewItem)keyboardFocused;
                                }
                                break;
                        }
                    }
                    break;

                default:
                    {
                        // store current control
                        if (_gamepadPage.ContainsKey(window))
                            prevControl[_gamepadPage[window].Tag] = keyboardFocused;
                    }
                    break;
            }

            if (keyboardFocused is not null)
            {
                // pick the last known Control
                return keyboardFocused;
            }
            else if (window.GetType() == typeof(MainWindow))
            {
                // pick the top left NavigationViewItem
                return WPFUtils.GetTopLeftControl<NavigationViewItem>(window.elements);
            }
            else if (window.GetType() == typeof(OverlayQuickTools))
            {
                // pick the top left Control
                return WPFUtils.GetTopLeftControl<Control>(window.elements);
            }

            return null;
        }

        public static void Start()
        {
        }

        private static void InputsUpdated(ControllerState controllerState)
        {
            if (_currentWindow is null || !_rendered)
                return;

            // stop gamepad navigation when InputsManager is listening
            if (InputsManager.IsListening())
                return;

            if (controllerState.ButtonState.Equals(prevButtonState))
                return;

            prevButtonState = controllerState.ButtonState.Clone() as ButtonState;

            // UI thread (async)
            Application.Current.Dispatcher.BeginInvoke(() =>
            {

                // get current focused element
                Control focusedElement = FocusedElement(_currentWindow);
                if (focusedElement is null)
                    return;

                string elementType = focusedElement.GetType().Name;

                // set direction
                WPFUtils.Direction direction = WPFUtils.Direction.None;

                // force display keyboard focus rectangle
                WPFUtils.MakeFocusVisible(_currentWindow);

                if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.B1))
                {
                    // lazy
                    // todo: implement proper RoutedEvent call
                     switch (elementType)
                    {
                        case "Button":
                        case "ToggleSwitch":
                        case "ToggleButton":
                        case "CheckBox":
                            {
                                KeyboardSimulator.KeyPress(VirtualKeyCode.SPACE);
                            }
                            break;

                        case "NavigationViewItem":
                            {
                                switch(focusedElement.Name)
                                {
                                    case "b_ServiceStart":
                                    case "b_ServiceStop":
                                    case "b_ServiceInstall":
                                    case "b_ServiceDelete":
                                        {
                                            KeyboardSimulator.KeyPress(VirtualKeyCode.SPACE);
                                        }
                                        return;
                                    default:
                                        {
                                            // set state
                                            _goingForward = true;

                                            if (prevControl.TryGetValue(_gamepadPage[_currentWindow].Tag, out Control control))
                                                Focus(control);
                                            else
                                            {
                                                // get the nearest non-navigation control
                                                focusedElement = WPFUtils.GetTopLeftControl<Control>(_currentWindow.elements);
                                                Focus(focusedElement);
                                            }
                                        }
                                        return;
                                }
                            }
                            break;

                        case "ComboBox":
                            {
                                ComboBox comboBox = (ComboBox)focusedElement;
                                comboBox.IsDropDownOpen = true;
                            }
                            return;

                        case "ComboBoxItem":
                            {
                                KeyboardSimulator.KeyPress(VirtualKeyCode.RETURN);
                            }
                            return;
                    }
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.B2))
                {
                    // lazy
                    // todo: implement proper RoutedEvent call
                    switch (elementType)
                    {
                        default:
                            {
                                switch (_gamepadPage[_currentWindow].Tag)
                                {
                                    default:
                                        {
                                            // restore previous NavigationViewItem
                                            Focus(prevNavigation[_currentWindow.Tag]);
                                        }
                                        return;

                                    case "layout":
                                    case "SettingsMode0":
                                    case "SettingsMode1":
                                        {
                                            // set state
                                            _goingBack = true;

                                            // go back to previous page
                                            _gamepadFrame[_currentWindow].GoBack();
                                        }
                                        return;
                                }
                            }
                            break;

                        case "ComboBoxItem":
                            {
                                ComboBox comboBox = ItemsControl.ItemsControlFromItemContainer(focusedElement) as ComboBox;
                                comboBox.IsDropDownOpen = false;
                            }
                            return;

                        case "NavigationViewItem":
                            {
                            }
                            break;
                    }
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.L1))
                {
                    if (prevNavigation.TryGetValue(_currentWindow.Tag, out focusedElement))
                    {
                        elementType = focusedElement.GetType().Name;
                        direction = WPFUtils.Direction.Left;
                    }
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.R1))
                {
                    if (prevNavigation.TryGetValue(_currentWindow.Tag, out focusedElement))
                    {
                        elementType = focusedElement.GetType().Name;
                        direction = WPFUtils.Direction.Right;
                    }
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.DPadUp) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftThumbUp) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftPadClickUp))
                {
                    direction = WPFUtils.Direction.Up;
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.DPadDown) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftThumbDown) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftPadClickDown))
                {
                    direction = WPFUtils.Direction.Down;
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.DPadLeft) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftThumbLeft) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftPadClickLeft))
                {
                    direction = WPFUtils.Direction.Left;
                }
                else if (controllerState.ButtonState.Buttons.Contains(ButtonFlags.DPadRight) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftThumbRight) || controllerState.ButtonState.Buttons.Contains(ButtonFlags.LeftPadClickRight))
                {
                    direction = WPFUtils.Direction.Right;
                }

                // navigation
                if (direction != WPFUtils.Direction.None)
                {
                    switch(elementType)
                    {
                        case "NavigationViewItem":
                            {
                                focusedElement = WPFUtils.GetClosestControl<NavigationViewItem>(focusedElement, _currentWindow.elements, direction);
                                Focus(focusedElement);
                            }
                            return;

                        case "ComboBox":
                            {
                                ComboBox comboBox = (ComboBox)focusedElement;
                                if (comboBox.IsDropDownOpen)
                                {
                                    switch (direction)
                                    {
                                        case WPFUtils.Direction.Up:
                                            KeyboardSimulator.KeyPress(VirtualKeyCode.UP);
                                            return;
                                        case WPFUtils.Direction.Down:
                                            KeyboardSimulator.KeyPress(VirtualKeyCode.DOWN);
                                            return;
                                    }
                                }
                            }
                            break;

                        case "ComboBoxItem":
                            {
                                switch(direction)
                                {
                                    case WPFUtils.Direction.Up:
                                        KeyboardSimulator.KeyPress(VirtualKeyCode.UP);
                                        return;
                                    case WPFUtils.Direction.Down:
                                        KeyboardSimulator.KeyPress(VirtualKeyCode.DOWN);
                                        return;
                                }
                            }
                            break;

                        case "Slider":
                            {
                                switch (direction)
                                {
                                    case WPFUtils.Direction.Up:
                                    case WPFUtils.Direction.Down:
                                        focusedElement = WPFUtils.GetClosestControl<Control>(focusedElement, _currentWindow.elements, direction, new List<Type>() { typeof(NavigationViewItem) });
                                        Focus(focusedElement);
                                        return;

                                    case WPFUtils.Direction.Left:
                                        KeyboardSimulator.KeyPress(VirtualKeyCode.LEFT);
                                        return;
                                    case WPFUtils.Direction.Right:
                                        KeyboardSimulator.KeyPress(VirtualKeyCode.RIGHT);
                                        return;
                                }
                            }
                            break;
                    }

                    // default
                    focusedElement = WPFUtils.GetClosestControl<Control>(focusedElement, _currentWindow.elements, direction, new List<Type>() { typeof(NavigationViewItem) });
                    Focus(focusedElement);
                }
            });
        }
    }
}