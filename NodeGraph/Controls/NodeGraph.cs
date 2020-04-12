﻿using NodeGraph.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NodeGraph.Controls
{
    public class NodeGraph : MultiSelector
    {
        public Canvas Canvas { get; private set; } = null;

        public Key MoveWithKey
        {
            get => (Key)GetValue(MoveWithKeyProperty);
            set => SetValue(MoveWithKeyProperty, value);
        }
        public static readonly DependencyProperty MoveWithKeyProperty =
            DependencyProperty.Register(nameof(MoveWithKey), typeof(Key), typeof(NodeGraph), new FrameworkPropertyMetadata(Key.None));

        public MouseButton MoveWithMouse
        {
            get => (MouseButton)GetValue(MoveWithMouseProperty);
            set => SetValue(MoveWithMouseProperty, value);
        }
        public static readonly DependencyProperty MoveWithMouseProperty =
            DependencyProperty.Register(nameof(MoveWithMouse), typeof(MouseButton), typeof(NodeGraph), new FrameworkPropertyMetadata(MouseButton.Middle));

        public Key ScaleWithKey
        {
            get => (Key)GetValue(ScaleWithKeyProperty);
            set => SetValue(ScaleWithKeyProperty, value);
        }
        public static readonly DependencyProperty ScaleWithKeyProperty =
            DependencyProperty.Register(nameof(ScaleWithKey), typeof(Key), typeof(NodeGraph), new FrameworkPropertyMetadata(Key.None));

        public double Scale
        {
            get => (double)GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }
        public static readonly DependencyProperty ScaleProperty =
            DependencyProperty.Register(nameof(Scale), typeof(double), typeof(NodeGraph), new FrameworkPropertyMetadata(1.0));

        public double MinScale
        {
            get => (double)GetValue(MinScaleProperty);
            set => SetValue(MinScaleProperty, value);
        }
        public static readonly DependencyProperty MinScaleProperty =
            DependencyProperty.Register(nameof(MinScale), typeof(double), typeof(NodeGraph), new FrameworkPropertyMetadata(0.1));

        public double ScaleRate
        {
            get => (double)GetValue(ScaleRateProperty);
            set => SetValue(ScaleRateProperty, value);
        }
        public static readonly DependencyProperty ScaleRateProperty =
            DependencyProperty.Register(nameof(ScaleRate), typeof(double), typeof(NodeGraph), new FrameworkPropertyMetadata(0.1));

        public Point Offset
        {
            get => (Point)GetValue(OffsetProperty);
            set => SetValue(OffsetProperty, value);
        }
        public static readonly DependencyProperty OffsetProperty =
            DependencyProperty.Register(nameof(Offset), typeof(Point), typeof(NodeGraph), new FrameworkPropertyMetadata(new Point(0, 0), OffsetPropertyChanged));

        public ICommand PreviewConnectCommand
        {
            get => (ICommand)GetValue(PreviewConnectCommandProperty);
            set => SetValue(PreviewConnectCommandProperty, value);
        }
        public static readonly DependencyProperty PreviewConnectCommandProperty =
            DependencyProperty.Register(nameof(PreviewConnectCommand), typeof(ICommand), typeof(NodeGraph), new FrameworkPropertyMetadata(null));

        ControlTemplate NodeTemplate => _NodeTemplate.Get("__NodeTemplate__");
        ResourceInstance<ControlTemplate> _NodeTemplate = new ResourceInstance<ControlTemplate>();

        Style NodeBaseStyle => _NodeBaseStyle.Get("__NodeBaseStyle__");
        ResourceInstance<Style> _NodeBaseStyle = new ResourceInstance<Style>();

        bool _IsNodeSelected = false;
        bool _IsStartDragging = false;
        bool _PressKeyToMove = false;
        bool _PressMouseToMove = false;
        bool _PressMouseToSelect = false;
        bool _IsRangeSelecting = false;

        NodeLink _DraggingNodeLink = null;
        List<Node> _DraggingNodes = new List<Node>();
        NodeConnectorContent _DraggingConnector = null;
        Point _DragStartPointToMoveNode = new Point();
        Point _DragStartPointToMoveOffset = new Point();
        Point _CaptureOffset = new Point();

        Point _DragStartPointToSelect = new Point();
        RangeSelector _RangeSelector = new RangeSelector();

        NodeLink _ReconnectingNodeLink = null;

        HashSet<NodeConnectorContent> _PreviewedConnectors = new HashSet<NodeConnectorContent>();

        List<object> _DelayToBindVMs = new List<object>();

        static void OffsetPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var nodeGraph = d as NodeGraph;

            // need to calculate node position before calculate link absolute position.
            foreach (var obj in nodeGraph.Canvas.Children.OfType<Node>())
            {
                obj.UpdateOffset(nodeGraph.Offset);
            }

            foreach (var obj in nodeGraph.Canvas.Children.OfType<NodeLink>())
            {
                obj.UpdateOffset(nodeGraph.Offset);
            }
        }

        static NodeGraph()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NodeGraph), new FrameworkPropertyMetadata(typeof(NodeGraph)));
        }

        public Point GetDragNodePosition(MouseEventArgs e)
        {
            return e.GetPosition(Canvas);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Canvas = GetTemplateChild("__NodeGraphCanvas__") as Canvas;

            if (_DelayToBindVMs.Count > 0)
            {
                AddNodesToCanvas(_DelayToBindVMs.OfType<object>());
                _DelayToBindVMs.Clear();
            }
        }

        protected override void OnItemContainerStyleChanged(Style oldItemContainerStyle, Style newItemContainerStyle)
        {
            base.OnItemContainerStyleChanged(oldItemContainerStyle, newItemContainerStyle);
            if (newItemContainerStyle != null)
            {
                newItemContainerStyle.BasedOn = NodeBaseStyle;
            }
        }

        protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
        {
            base.OnItemsSourceChanged(oldValue, newValue);

            if (Canvas == null)
            {
                _DelayToBindVMs.AddRange(newValue.OfType<object>());
            }
            else
            {
                AddNodesToCanvas(newValue.OfType<object>());
            }

            if (oldValue != null && oldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= NodeCollectionChanged;
            }
            if (newValue != null && newValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += NodeCollectionChanged;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (MoveWithKey == Key.None || e.Key == MoveWithKey)
            {
                _PressKeyToMove = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            _PressKeyToMove = false;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (ScaleWithKey == Key.None || (Keyboard.GetKeyStates(ScaleWithKey) & KeyStates.Down) != 0)
            {
                Scale += e.Delta > 0 ? +ScaleRate : -ScaleRate;
                Scale = Math.Max(MinScale, Scale);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var posOnCanvas = e.GetPosition(Canvas);

            if (_DraggingNodes.Count > 0)
            {
                if (_IsStartDragging == false)
                {
                    if (_DraggingNodes[0].IsSelected)
                    {
                        foreach (var node in Canvas.Children.OfType<Node>().Where(arg => arg != _DraggingNodes[0] && arg.IsSelected))
                        {
                            node.CaptureDragStartPosition();
                            _DraggingNodes.Add(node);
                        }
                    }
                    else
                    {
                        _DraggingNodes[0].IsSelected = true; // select first drag node.

                        foreach (var node in Canvas.Children.OfType<Node>())
                        {
                            if (node != _DraggingNodes[0])
                            {
                                node.IsSelected = false;
                            }
                        }
                    }

                    _IsStartDragging = true;
                }

                var current = e.GetPosition(Canvas);
                var diff = new Point(current.X - _DragStartPointToMoveNode.X, current.Y - _DragStartPointToMoveNode.Y);

                foreach (var node in _DraggingNodes)
                {
                    double x = node.DragStartPosition.X + diff.X;
                    double y = node.DragStartPosition.Y + diff.Y;
                    node.UpdatePosition(x, y);
                }
            }
            else if (_PressMouseToMove && (MoveWithKey == Key.None || _PressKeyToMove))
            {
                var x = _CaptureOffset.X + posOnCanvas.X - _DragStartPointToMoveOffset.X;
                var y = _CaptureOffset.Y + posOnCanvas.Y - _DragStartPointToMoveOffset.Y;
                Offset = new Point(x, y);
            }
            else if (_DraggingNodeLink != null)
            {
                foreach(var connector in _PreviewedConnectors)
                {
                    connector.CanConnect = true;
                }
                _PreviewedConnectors.Clear();

                VisualTreeHelper.HitTest(Canvas, null, new HitTestResultCallback(arg =>
                {
                    var element = arg.VisualHit as FrameworkElement;
                    if(element != null && element.Tag is NodeConnectorContent connector && _DraggingConnector != connector)
                    {
                        PreviewConnect(connector);
                        _PreviewedConnectors.Add(connector);
                        return HitTestResultBehavior.Stop;
                    }
                    return HitTestResultBehavior.Continue;
                }), new PointHitTestParameters(posOnCanvas));

                _DraggingNodeLink.UpdateEdgePoint(posOnCanvas.X, posOnCanvas.Y);
            }
            else if (_ReconnectingNodeLink != null)
            {
                _ReconnectingNodeLink.UpdateEdgePoint(posOnCanvas.X, posOnCanvas.Y);
            }
            else if (_PressMouseToSelect)
            {
                if (_IsRangeSelecting == false)
                {
                    Canvas.Children.Add(_RangeSelector);

                    _IsRangeSelecting = true;
                }

                _RangeSelector.RangeRect = new Rect(_DragStartPointToSelect, posOnCanvas);

                bool anyIntersects = false;
                foreach (var node in Canvas.Children.OfType<Node>())
                {
                    var nodeRect = new Rect(new Size(node.ActualWidth, node.ActualHeight));
                    var boundingBox = node.RenderTransform.TransformBounds(nodeRect);
                    node.IsSelected = _RangeSelector.RangeRect.IntersectsWith(boundingBox);

                    anyIntersects |= node.IsSelected;
                }
                _RangeSelector.IsIntersects = anyIntersects;
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            var posOnCanvas = e.GetPosition(Canvas);

            switch (MoveWithMouse)
            {
                case MouseButton.Left:
                    if (MoveWithKey == Key.None)
                    {
                        throw new InvalidProgramException("Not allow combination that no set MoveWithKey(Key.None) and left click.");
                    }

                    if (Keyboard.GetKeyStates(MoveWithKey) == KeyStates.Down)
                    {
                        // offset center focus.
                        _PressMouseToMove = e.LeftButton == MouseButtonState.Pressed;
                    }
                    break;
                case MouseButton.Middle:
                    _PressMouseToMove = e.MiddleButton == MouseButtonState.Pressed;
                    break;
                case MouseButton.Right:
                    _PressMouseToMove = e.RightButton == MouseButtonState.Pressed;
                    break;
            }

            if (e.LeftButton == MouseButtonState.Pressed && (MoveWithKey == Key.None || Keyboard.GetKeyStates(MoveWithKey) != KeyStates.Down))
            {
                // start to select by range rect.
                _PressMouseToSelect = true;
                _RangeSelector.Reset(posOnCanvas);

                _DragStartPointToSelect = posOnCanvas;
            }

            if (_PressMouseToMove)
            {
                _CaptureOffset = Offset;
                _DragStartPointToMoveOffset = posOnCanvas;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            _IsStartDragging = false;
            _PressMouseToMove = false;
            _PressMouseToSelect = false;

            if (_IsRangeSelecting)
            {
                Canvas.Children.Remove(_RangeSelector);
                _IsRangeSelecting = false;
            }
            else if (_IsNodeSelected == false)
            {
                // click empty area to unselect nodes.
                foreach (var node in Canvas.Children.OfType<Node>())
                {
                    node.IsSelected = false;
                }
            }
            _IsNodeSelected = false;
            _DraggingNodes.Clear();

            var element = e.OriginalSource as FrameworkElement;

            // clicked connect or not.
            if (_DraggingNodeLink != null && _DraggingConnector != null)
            {
                // only be able to connect input to output or output to input.
                // it will reject except above condition.
                if (element != null && element.Tag is NodeConnectorContent connector &&
                    _DraggingConnector.CanConnectTo(connector) && connector.CanConnectTo(_DraggingConnector))
                {
                    var transformer = element.TransformToVisual(Canvas);

                    switch (connector)
                    {
                        case NodeInputContent input:
                            ConnectNodeLink(element, input, _DraggingConnector as NodeOutputContent, _DraggingNodeLink, connector.GetContentPosition(Canvas));
                            _DraggingNodeLink.Connect(input);
                            break;
                        case NodeOutputContent output:
                            ConnectNodeLink(element, _DraggingConnector as NodeInputContent, output, _DraggingNodeLink, connector.GetContentPosition(Canvas));
                            _DraggingNodeLink.Connect(output);
                            break;
                        default:
                            throw new InvalidCastException();
                    }
                }
                else
                {
                    Canvas.Children.Remove(_DraggingNodeLink);
                    _DraggingNodeLink.Dispose();
                }

                _DraggingNodeLink = null;
                _DraggingConnector = null;

                ClearPreviewedConnect();
            }

            if (_ReconnectingNodeLink != null)
            {
                if (element != null && element.Tag is NodeInputContent input &&
                    input.CanConnectTo(_ReconnectingNodeLink.Output) && _ReconnectingNodeLink.Output.CanConnectTo(input))
                {
                    if (input != _ReconnectingNodeLink.Input)
                    {
                        _ReconnectingNodeLink.MouseDown -= NodeLink_MouseDown;

                        ConnectNodeLink(element, input, _ReconnectingNodeLink.Output, _ReconnectingNodeLink, input.GetContentPosition(Canvas));

                        _ReconnectingNodeLink.Connect(input);
                    }
                    else
                    {
                        _ReconnectingNodeLink.RestoreEndPoint();
                    }
                    _ReconnectingNodeLink = null;
                }
                else
                {
                    Canvas.Children.Remove(_ReconnectingNodeLink);

                    _ReconnectingNodeLink.MouseDown -= NodeLink_MouseDown;
                    _ReconnectingNodeLink.Disconnect();
                    _ReconnectingNodeLink.Dispose();
                    _ReconnectingNodeLink = null;
                }
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);

            _PressMouseToSelect = false;
            _PressMouseToMove = false;
            _IsStartDragging = false;
            _DraggingConnector = null;
            _DraggingNodes.Clear();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            _PressMouseToSelect = false;
            _PressMouseToMove = false;
            _IsStartDragging = false;
            _DraggingConnector = null;
            _DraggingNodes.Clear();
            InvalidateVisual();
        }

        void ClearPreviewedConnect()
        {
            foreach (var connector in _PreviewedConnectors)
            {
                connector.CanConnect = true;
            }
            _PreviewedConnectors.Clear();
        }

        void ConnectNodeLink(FrameworkElement element, NodeInputContent input, NodeOutputContent output, NodeLink nodeLink, Point point)
        {
            nodeLink.UpdateEdgePoint(point.X, point.Y);

            input.Connect(nodeLink);
            output.Connect(nodeLink);

            // add node link.
            nodeLink.MouseDown += NodeLink_MouseDown;
        }

        void NodeCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems?.Count > 0)
            {
                RemoveNodesFromCanvas(e.OldItems.OfType<object>());
            }
            if (e.NewItems?.Count > 0)
            {
                AddNodesToCanvas(e.NewItems.OfType<object>());
            }
        }

        void RemoveNodesFromCanvas(IEnumerable<object> removeVMs)
        {
            var removeElements = new List<Node>();
            var children = Canvas.Children.OfType<Node>();

            foreach (var removeVM in removeVMs)
            {
                var removeElement = children.First(arg => arg.DataContext == removeVM);
                removeElements.Add(removeElement);
            }

            foreach (var removeElement in removeElements)
            {
                Canvas.Children.Remove(removeElement);

                removeElement.MouseUp -= Node_MouseUp;
                removeElement.MouseDown -= Node_MouseDown;
                removeElement.Dispose();
            }
        }

        void AddNodesToCanvas(IEnumerable<object> addVMs)
        {
            foreach (var vm in addVMs)
            {
                var node = new Node(Canvas, Offset, Scale)
                {
                    DataContext = vm,
                    Template = NodeTemplate,
                    Style = ItemContainerStyle
                };

                node.MouseDown += Node_MouseDown;
                node.MouseUp += Node_MouseUp;

                Canvas.Children.Add(node);
            }
        }

        void PreviewConnect(NodeConnectorContent connector)
        {
            var connectTo = ConnectorType.Input;

            Guid inputNodeGuid;
            Guid inputGuid;
            if (_DraggingNodeLink.Input != null)
            {
                inputNodeGuid = _DraggingNodeLink.Input.Node.Guid;
                inputGuid = _DraggingNodeLink.Input.Guid;
                connectTo = ConnectorType.Output;
            }
            else
            {
                inputNodeGuid = connector.Node.Guid;
                inputGuid = connector.Guid;
            }

            Guid outputNodeGuid = Guid.Empty;
            Guid outputGuid = Guid.Empty;
            if (_DraggingNodeLink.Output != null)
            {
                outputNodeGuid = _DraggingNodeLink.Output.Node.Guid;
                outputGuid = _DraggingNodeLink.Output.Guid;
                connectTo = ConnectorType.Input;
            }
            else
            {
                outputNodeGuid = connector.Node.Guid;
                outputGuid = connector.Guid;
            }

            var param = new PreviewConnectCommandParameter(connectTo, inputNodeGuid, inputGuid, outputNodeGuid, outputGuid);
            PreviewConnectCommand.Execute(param);
            connector.CanConnect = param.CanConnect;
        }

        void NodeLink_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _ReconnectingNodeLink = sender as NodeLink;
            _ReconnectingNodeLink.ReleaseEndPoint();
        }

        void Node_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _IsNodeSelected = true;

            if (_IsStartDragging)
            {
                return;
            }

            if ((Keyboard.GetKeyStates(Key.LeftCtrl) & KeyStates.Down) != 0)
            {
                var node = sender as Node;
                node.IsSelected = !node.IsSelected;
            }
            else
            {
                foreach (var node in Canvas.Children.OfType<Node>())
                {
                    if (node != sender)
                    {
                        node.IsSelected = false;
                    }
                    else
                    {
                        node.IsSelected = true;
                    }
                }
            }
        }

        void Node_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;

            if (element != null && element.Tag is NodeConnectorContent connector)
            {
                // clicked on connector
                var posOnCanvas = connector.GetContentPosition(Canvas);

                switch (connector)
                {
                    case NodeOutputContent outputContent:
                        _DraggingNodeLink = new NodeLink(Canvas, posOnCanvas.X, posOnCanvas.Y, Scale, outputContent);
                        break;
                    case NodeInputContent inputContent:
                        _DraggingNodeLink = new NodeLink(Canvas, posOnCanvas.X, posOnCanvas.Y, Scale, inputContent);
                        break;
                    default:
                        throw new InvalidCastException();
                }

                _DraggingConnector = connector;
                Canvas.Children.Add(_DraggingNodeLink);
            }
            else if (IsOffsetMoveWithMouse(e) == false)
            {
                // clicked on Node
                var pos = e.GetPosition(Canvas);

                var firstNode = e.Source as Node;
                firstNode.CaptureDragStartPosition();

                _DraggingNodes.Add(firstNode);

                _DragStartPointToMoveNode = e.GetPosition(Canvas);
            }
        }

        bool IsOffsetMoveWithMouse(MouseButtonEventArgs e)
        {
            switch (MoveWithMouse)
            {
                case MouseButton.Left:
                    return e.LeftButton == MouseButtonState.Pressed;
                case MouseButton.Middle:
                    return e.MiddleButton == MouseButtonState.Pressed;
                case MouseButton.Right:
                    return e.RightButton == MouseButtonState.Pressed;
                default:
                    throw new InvalidCastException();
            }
        }
    }
}
