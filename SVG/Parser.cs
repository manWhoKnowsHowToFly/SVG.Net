using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SVG {
    public class Parser : IRenderTargetProvider {
        List<Path> pathsCore = new List<Path>();
        public IReadOnlyList<Path> Paths {
            get { return pathsCore; }
        }
        public bool IsReady { get; private set; }
        public void Parse(XDocument document) {
            var pathElements = from node in document.Root.Descendants()
                               where node.Name.LocalName == "path"
                               select node;
            foreach(var pathElement in pathElements) {
                Path parseablePath = Path.Parse(pathElement);
                pathsCore.Add(parseablePath);
            }
            IsReady = true;
        }
        public void SetDirty() {
            IsReady = false;
        }
    }

    public interface IRenderTargetProvider {
        IReadOnlyList<Path> Paths { get; }
    }
    public class PathLocationInfo {
        PointF absLocation = PointF.Empty;
        PointF relLocation = PointF.Empty;
        PointF[] lastCurveLocations;
        CurveType lastCurveOperationType;
        public PointF GetActualLocation(Location location) {
            Update(location);
            return GetActualLocation(location.UseRelativeCoordinates);
        }
        public PointF GetActualLocation(bool useRelativeCoordinates) {
            if(useRelativeCoordinates)
                return new PointF(absLocation.X + relLocation.X, absLocation.Y + relLocation.Y);
            return absLocation;
        }
        public void Update(Location location) {
            SetLastCurveOperationType(location);
            if(location.UseRelativeCoordinates)
                UpdateCore(ref relLocation, location);
            else
                UpdateCore(ref absLocation, location);
        }
        void SetLastCurveOperationType(Location location) {
            switch(location.CommandType) {
                case PathCommandType.CurveTo:
                case PathCommandType.SmoothCurveTo: lastCurveOperationType = CurveType.Cubic; break;
                case PathCommandType.QuadraticCurveTo:
                case PathCommandType.SmoothQuadraticCurveTo: lastCurveOperationType = CurveType.Quadratic; break;
                default: lastCurveOperationType = CurveType.None; break;
            }
        }
        protected virtual void UpdateCore(ref PointF location, Location assignLocation) {
            if((assignLocation.UnitType & LocationUnitType.X) != 0)
                location.X = assignLocation.X;
            if((assignLocation.UnitType & LocationUnitType.Y) != 0)
                location.Y = assignLocation.Y;
        }
        public virtual void UpdateLastCurveControlPoints(CurveType curveType) {
            lastCurveOperationType = curveType;
        }
        public virtual void UpdateLastCurveControlPoints(CurveType curveType , params PointF[] controlPoints) {
            lastCurveOperationType = curveType;
            lastCurveLocations = controlPoints;
        }
        public virtual bool HasSavedLastCurveLocations(CurveType curveType) {
            return curveType == lastCurveOperationType;
        }
        public virtual Location CalcCurveControlPoint(Location curveEndPoint, Location endControlPoint = default(Location)) {
            PointF currentLocation = GetActualLocation(curveEndPoint.UseRelativeCoordinates);
            PointF lastCurveControlPoint = lastCurveLocations.Last();
            PointF[] vectors = new PointF[2];
            PointF[] points = new PointF[2];
            if(lastCurveOperationType == CurveType.Cubic) {
                vectors[0] = new PointF(currentLocation.X - lastCurveControlPoint.X, currentLocation.Y - lastCurveControlPoint.Y);
                points[0] = lastCurveControlPoint;
                vectors[1] = new PointF(endControlPoint.X - currentLocation.X, endControlPoint.Y - currentLocation.Y);
                points[1] = endControlPoint;
            }
            else {
                PointF prevCurveStartLocation = lastCurveLocations[0];
                vectors[0] = new PointF(lastCurveControlPoint.X - prevCurveStartLocation.X, lastCurveControlPoint.Y - prevCurveStartLocation.Y);
                points[0] = curveEndPoint;
                vectors[1] = new PointF(currentLocation.X - lastCurveControlPoint.X, currentLocation.Y - lastCurveControlPoint.Y);
                points[1] = currentLocation;
            }
            PointF result = CalcCurveControlPointCore(vectors, points);
            return new Location(result, curveEndPoint.CommandType, this, curveEndPoint.UseRelativeCoordinates); ;
        }
        PointF CalcCurveControlPointCore(PointF[] v, params PointF[] p) {
            return new PointF()
            {
                X = (v[1].X * v[0].Y * p[0].X - v[1].X * v[0].X * p[0].Y - v[0].X * v[1].Y * p[1].X + v[0].X * v[1].X * p[1].Y) / (v[1].X * v[0].Y - v[0].X * v[1].Y),
                Y = (v[1].Y * v[0].X * p[0].Y - v[1].Y * v[0].Y * p[0].X - v[0].Y * v[1].X * p[1].Y + v[0].Y * v[1].Y * p[0].X) / (v[1].Y * v[0].X - v[0].Y * v[1].X)
            };
        }
        public enum CurveType {
            None, Cubic, Quadratic
        }
    }

    public class Path {
        PathLocationInfo locationInfoCore = new PathLocationInfo();
        List<PathCommand> pathCommandsCore = new List<PathCommand>();
        PathAppearance pathAppearanceCore = new PathAppearance();
        public PathLocationInfo LocationInfo { get { return locationInfoCore; } }
        public IReadOnlyList<PathCommand> PathCommands { get { return pathCommandsCore; } }
        public PathAppearance PathAppearance { get { return pathAppearanceCore; } }
        public static Path Parse(XElement pathElement) {
            Path path = new Path();
            path.ParseCore(pathElement);
            return path;
        }
        protected void ParseCore(XElement pathElement) {
            ParsePathDataCore(GetPathData(pathElement));
            ParsePathStylesCore(GetStyles(pathElement));
        }
        string GetPathData(XElement pathElement) {
            foreach(var attribute in pathElement.Attributes()) {
                if(attribute.Name == "d")
                    return attribute.Value;
            }
            return String.Empty;
        }
        Dictionary<string, string> GetStyles(XElement pathElement) {
            Dictionary<string, string> styles = new Dictionary<string, string>();
            foreach(var attribute in pathElement.Attributes()) {
                if(attribute.Name != "d")
                    styles.Add(attribute.Name.LocalName, attribute.Value);
            }
            return styles;
        }
        protected virtual void ParsePathDataCore(string pathData) {
            for(int i = 0, next = 0; i < pathData.Length; i = next) {
                if(!CheckCommand(pathData[i])) {
                    next += 1;
                    continue;
                }
                next = GetNextCommandPosition(pathData, i);
                if(i == next) break;
                ParsePathCommand(pathData.Substring(i, next - i - 1));
            }
        }
        protected virtual void ParsePathCommand(string commandData) {
            PathCommand command = PathCommand.Parse(commandData);
            pathCommandsCore.Add(command);
        }
        protected virtual void ParsePathStylesCore(Dictionary<string, string> styles) {
            pathAppearanceCore = PathAppearance.Parse(styles);
        }
        int GetNextCommandPosition(string sequence, int index) {
            for(int i = index; i < sequence.Length; i++)
                if(CheckCommand(sequence[i]) && i > index)
                    return i;
            return index;
        }
        bool CheckCommand(char sym) {
            return Char.IsLetter(sym);
        }
    }
    public class PathAppearance {
        public Color BackColor { get; set; }
        public Color ForeColor { get; set; }
        public int Thickness { get; set; }
        public static PathAppearance Parse(Dictionary<string, string> styles) {
            PathAppearance pathStyle = new PathAppearance();
            pathStyle.ParseCore(styles);
            return pathStyle;
        }
        protected virtual void ParseCore(Dictionary<string, string> styles) {
            BackColor = styles.ContainsKey("fill") ? ParseColor(styles["fill"]) : Color.Empty;
            ForeColor = styles.ContainsKey("stroke") ? ParseColor(styles["stroke"]) : Color.Black;
            Thickness = styles.ContainsKey("stroke-width") ? int.Parse(styles["stroke-width"]) : 1;
        }
        Color ParseColor(string str) {
            Color color = Color.FromName(str);
            if(color.IsEmpty) {
                int colorValue = int.Parse(str.Replace('#', ' ').Trim(), CultureInfo.InvariantCulture);
                color = Color.FromArgb(colorValue);
            }
            return color;
        }
        public void DrawPath(Graphics g, GraphicsPath path) {
            if(!BackColor.IsEmpty) {
                g.FillPath(new SolidBrush(BackColor), path);
            }
            if(!ForeColor.IsEmpty) {
                GraphicsPath cpath = (GraphicsPath)path.Clone();
                cpath.Widen(new Pen(ForeColor, Thickness));
                g.FillPath(new SolidBrush(ForeColor), path);
            }
            else
                g.DrawPath(Pens.Black, path);

        }
    }
    public enum PathCommandType {
        MoveTo, LineTo, HorizontalLineTo, VerticalLineTo, CurveTo, SmoothCurveTo, QuadraticCurveTo, SmoothQuadraticCurveTo, EllipticalArc, ClosePath
    }
    public class PathCommand {
        public PathCommandType Type { get; protected set; }
        public PathCommandArgs Args { get; protected set; }
        public static PathCommand Parse(string sequence, PathLocationInfo pathLocationInfo = null) {
            PathCommand pathCommand = new PathCommand();
            pathCommand.ParseCore(sequence);
            return pathCommand;
        }
        protected virtual void ParseCore(string sequence, PathLocationInfo pathLocationInfo = null) {
            Type = GetPathCommandType(sequence.First());
            Args = PathCommandArgs.Parse(Type, sequence);
        }
        PathCommandType GetPathCommandType(char cmd) {
            if(cmd == 'm' || cmd == 'M')
                return PathCommandType.MoveTo;
            if(cmd == 'l' || cmd == 'L')
                return PathCommandType.LineTo;
            if(cmd == 'h' || cmd == 'H')
                return PathCommandType.HorizontalLineTo;
            if(cmd == 'v' || cmd == 'V')
                return PathCommandType.VerticalLineTo;
            if(cmd == 'c' || cmd == 'C')
                return PathCommandType.CurveTo;
            if(cmd == 'q' || cmd == 'Q')
                return PathCommandType.QuadraticCurveTo;
            if(cmd == 's' || cmd == 'S')
                return PathCommandType.SmoothCurveTo;
            if(cmd == 't' || cmd == 'T')
                return PathCommandType.SmoothQuadraticCurveTo;
            if(cmd == 'a' || cmd == 'A')
                return PathCommandType.EllipticalArc;
            return PathCommandType.ClosePath;
        }
    }
    public class PathCommandArgs {
        PathCommandType commandTypeCore;
        public PathCommandArgs(PathCommandType commandType) {
            commandTypeCore = commandType;
        }
        public PathCommandType CommandType { get { return commandTypeCore; } }
        public PathLocationInfo PathLocationInfo { get; protected set; }
        public bool UseRelativeCoordinates { get; protected set; }
        public static PathCommandArgs Parse(PathCommandType commandType, string sequence, PathLocationInfo pathLocationInfo = null) {
            PathCommandArgs commandArgs = CreateInstance(commandType);
            commandArgs.PathLocationInfo = pathLocationInfo;
            commandArgs.UseRelativeCoordinates = Char.IsLower(sequence.First());
            commandArgs.ParseCore(sequence.Substring(1), pathLocationInfo);
            return commandArgs;
        }
        static PathCommandArgs CreateInstance(PathCommandType commandType) {
            switch(commandType) {
                case PathCommandType.MoveTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.LineTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.HorizontalLineTo: return new HorizontalLineToArgs(commandType);
                case PathCommandType.VerticalLineTo: return new VerticalLineToArgs(commandType);
                case PathCommandType.CurveTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.SmoothCurveTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.QuadraticCurveTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.SmoothQuadraticCurveTo: return new MoveToCommandArgs(commandType);
                case PathCommandType.EllipticalArc: return new EllipticalArcArgs(commandType);
                default: return new PathCommandArgs(commandType);
            }
        }
        protected virtual void ParseCore(string sequence, PathLocationInfo pathLocationInfo) {
            string[] coordinates = sequence.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            ParseCore(coordinates, pathLocationInfo);
        }
        protected virtual void ParseCore(string[] coordinates, PathLocationInfo pathLocationInfo) { }
    }
    public class MoveToCommandArgs : PathCommandArgs {
        protected List<Location> locationsCore;
        public MoveToCommandArgs(PathCommandType commandType) : base(commandType) {
            locationsCore = new List<Location>();
        }
        public MoveToCommandArgs(PathCommandType commandType, Location[] locations) : base(commandType) {
            locationsCore = locations.ToList();
        }
        public MoveToCommandArgs(PathCommandType commandType, Location[] locations, PathLocationInfo pathLocationInfo) : base(commandType) {
            locationsCore = locations.ToList();
            PathLocationInfo = pathLocationInfo;
        }
        public Location[] Locations { get { return locationsCore.ToArray(); } }
        public void AddLocation(Location location) {
            locationsCore.Add(location);
        }
        protected override void ParseCore(string[] coordinates, PathLocationInfo pathLocationInfo) {
            for(int i = 0, j = 0; i < coordinates.Length; i += 2, j++) {
                PointF point = new PointF(Convert.ToSingle(coordinates[i], CultureInfo.InvariantCulture), Convert.ToSingle(coordinates[i + 1], CultureInfo.InvariantCulture));
                Location location = new Location(point, CommandType, pathLocationInfo, UseRelativeCoordinates);
                AddLocation(location);
            }
        }
        protected virtual Location CreateLocation(PathLocationInfo pathLocationInfo) {
            return new Location(CommandType, pathLocationInfo, UseRelativeCoordinates);
        }
    }
    public class HorizontalLineToArgs : MoveToCommandArgs {
        public HorizontalLineToArgs(PathCommandType commandType) : base(commandType) { }
        protected override void ParseCore(string[] coordinates, PathLocationInfo pathLocationInfo) {
            foreach(string coordinate in coordinates) {
                PointF point = new PointF(0, Convert.ToSingle(coordinate, CultureInfo.InvariantCulture));
                Location location = new Location(point, CommandType, pathLocationInfo, UseRelativeCoordinates);
                location.UnitType = LocationUnitType.X;
                AddLocation(location);
            }
        }
    }
    public class VerticalLineToArgs : MoveToCommandArgs {
        public VerticalLineToArgs(PathCommandType commandType) : base(commandType) { }
        protected override void ParseCore(string[] coordinates, PathLocationInfo pathLocationInfo) {
            foreach(string coordinate in coordinates) {
                PointF point = new PointF(Convert.ToSingle(coordinate, CultureInfo.InvariantCulture), 0);
                Location location = new Location(point, CommandType, pathLocationInfo, UseRelativeCoordinates);
                location.UnitType = LocationUnitType.Y;
                AddLocation(location);
            }
        }
    }
    public class EllipticalArcArgs : PathCommandArgs {
        Location locationCore;
        SizeF radiusCore;
        public EllipticalArcArgs(PathCommandType commandType) : base(commandType) { }
        public Location Location { get { return locationCore; } }
        public SizeF Radius { get { return radiusCore; } }
        public bool XAxisRotation { get; protected set; }
        public bool LargeArcFlag { get; protected set; }
        public bool SweepFlag { get; protected set; }
        protected override void ParseCore(string[] args, PathLocationInfo pathLocationInfo) {
            radiusCore.Width = Convert.ToSingle(args[0], CultureInfo.InvariantCulture);
            radiusCore.Height = Convert.ToSingle(args[1], CultureInfo.InvariantCulture);
            XAxisRotation = Convert.ToBoolean(Convert.ToInt32(args[2], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            LargeArcFlag = Convert.ToBoolean(Convert.ToInt32(args[3], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            SweepFlag = Convert.ToBoolean(Convert.ToInt32(args[4], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            PointF location = new PointF(Convert.ToSingle(args[5], CultureInfo.InvariantCulture), Convert.ToSingle(args[6], CultureInfo.InvariantCulture));
            locationCore = new Location(location, CommandType, pathLocationInfo, UseRelativeCoordinates);
        }
    }
}