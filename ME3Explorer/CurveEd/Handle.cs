﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ME3Explorer.CurveEd
{

    class Handle : Thumb
    {
        public const double HANDLE_LENGTH = 30f;
        private const double angleCutoff = 90 * (Math.PI / 180);
        public Anchor anchor;

        private bool Left;

        public double Y
        {
            get { return (double)GetValue(YProperty); }
            set { SetValue(YProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Y.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty YProperty =
            DependencyProperty.Register("Y", typeof(double), typeof(Handle), new PropertyMetadata(0.0));

        public double X
        {
            get { return (double)GetValue(XProperty); }
            set { SetValue(XProperty, value); }
        }

        // Using a DependencyProperty as the backing store for X.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty XProperty =
            DependencyProperty.Register("X", typeof(double), typeof(Handle), new PropertyMetadata(0.0));

        public double Slope
        {
            get { return (double)GetValue(SlopeProperty); }
            set { SetValue(SlopeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Slope.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SlopeProperty =
            DependencyProperty.Register("Slope", typeof(double), typeof(Handle), new PropertyMetadata(0.0, OnSlopeChanged));

        private static void OnSlopeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            Handle h = sender as Handle;
            if (h != null)
            {
                if (h.Left)
                {
                    h.anchor.point.Value.ArriveTangent = Convert.ToSingle((double)e.NewValue);
                    if (h.anchor.leftBez != null)
                    {
                        h.anchor.leftBez.Slope2 = (double)e.NewValue;
                    }
                }
                else
                {
                    h.anchor.point.Value.LeaveTangent = Convert.ToSingle((double)e.NewValue);
                    if (h.anchor.rightBez != null)
                    {
                        h.anchor.rightBez.Slope1 = (double)e.NewValue;
                    }
                }
            }
        }

        public Handle(Anchor a, bool left)
        {
            anchor = a;
            Left = left;
            Slope = Left ? a.point.Value.ArriveTangent : a.point.Value.LeaveTangent;
            Line line = new Line();
            line.bind(Line.X1Property, a, "X");
            line.bind(Line.Y1Property, a, "Y", new YConverter(), a.graph.ActualHeight);
            line.bind(Line.X2Property, this, "X");
            line.bind(Line.Y2Property, this, "Y", new YConverter(), a.graph.ActualHeight);
            line.bind(Line.VisibilityProperty, this, "Visibility");
            line.Style = a.graph.FindResource("HandleLine") as Style;
            a.graph.graph.Children.Add(line); 
            this.DragDelta += OnDragDelta;

            double hScale = a.graph.HorizontalScale;
            double vScale = a.graph.VerticalScale;
            double xLength = (HANDLE_LENGTH * (Left ? -1 : 1)) / Math.Sqrt(Math.Pow(hScale, 2) + Math.Pow(Slope, 2) * Math.Pow(vScale, 2));
            X = xLength * hScale + a.X;
            Y = Slope * xLength * vScale + a.Y;
        }

        private void OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            switch (anchor.point.Value.InterpMode)
            {
                case CurveMode.CIM_CurveAuto:
                case CurveMode.CIM_CurveAutoClamped:
                    anchor.point.Value.InterpMode = CurveMode.CIM_CurveUser;
                    anchor.graph.invokeSelectedPointChanged();
                    break;
                case CurveMode.CIM_CurveUser:
                case CurveMode.CIM_CurveBreak:
                case CurveMode.CIM_Linear:
                case CurveMode.CIM_Constant:
                default:
                    break;
            }
            Point pos = Mouse.GetPosition(anchor.graph);
            double angle = Math.Atan2(anchor.graph.ActualHeight - pos.Y - anchor.Y, pos.X - anchor.X);
            if (Left && Math.Abs(angle) < angleCutoff + 0.01)
            {
                angle = (angleCutoff + 0.01) * Math.Sign(angle);
            }
            else if (!Left && Math.Abs(angle) > angleCutoff - 0.01)
            {
                angle = (angleCutoff - 0.01) * Math.Sign(angle);
            }
            double rise = HANDLE_LENGTH * Math.Sin(angle);
            double run = HANDLE_LENGTH * Math.Cos(angle);
            Y = anchor.Y + rise;
            X = anchor.X + run;
            Slope = (rise / anchor.graph.VerticalScale) / (run / anchor.graph.HorizontalScale);

            if (anchor.point.Value.InterpMode == CurveMode.CIM_CurveUser)
            {
                Handle otherHandle = Left ? anchor.rightHandle : anchor.leftHandle;
                otherHandle.X = anchor.X - run;
                otherHandle.Y = anchor.Y - rise;
                otherHandle.Slope = Slope;
            }
            
        }
    }
}
