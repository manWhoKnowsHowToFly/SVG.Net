using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SVG {
    [Flags]
    public enum LocationUnitType {
        All = 3, X = 2, Y = 1
    }
    public struct Location {
        PointF locationCore;
        PathLocationInfo pathLocationInfoCore;
        bool useRelativeCoordinatesCore;
        public Location(PointF location, PathCommandType commandType, PathLocationInfo locationInfo, bool useRelativeCoordinates) : this() {
            locationCore = location;
            pathLocationInfoCore = locationInfo;
            useRelativeCoordinatesCore = useRelativeCoordinates;
            CommandType = commandType;
        }
        public Location(PathCommandType commandType, PathLocationInfo locationInfo, bool useRelativeCoordinates) : this(PointF.Empty, commandType, locationInfo, useRelativeCoordinates) {
        }
        public static implicit operator PointF(Location location) {
            if(location.pathLocationInfoCore != null)
                return location.pathLocationInfoCore.GetActualLocation(location);
            return location.Point;
        }
        public PointF Point {
            get { return locationCore; }
            set { locationCore = value; }
        }
        public PathCommandType CommandType { get; set; }
        public LocationUnitType UnitType { get; set; }
        public float X { get { return locationCore.X; } set { locationCore.X = value; } }
        public float Y { get { return locationCore.Y; } set { locationCore.Y = value; } }
        public bool UseRelativeCoordinates {
            get { return useRelativeCoordinatesCore; }
            set { useRelativeCoordinatesCore = value; }
        }
        public void SetPathLocationInfo(PathLocationInfo pathLocationInfo) {
            pathLocationInfoCore = pathLocationInfo;
        }
    }
    public class RenderedPathInfo {
        GraphicsPath pathCore;
        PathLocationInfo locationInfoCore;
        PathAppearance appearanceCore;
        public RenderedPathInfo(PathAppearance appearance, PathLocationInfo pathLocationInfo = null) {
            appearanceCore = appearance;
            pathCore = new GraphicsPath();
            locationInfoCore = pathLocationInfo ?? new PathLocationInfo();
        }
        protected GraphicsPath Path { get { return pathCore; } }
        protected PathAppearance Appearance { get { return appearanceCore; } }
        protected PathLocationInfo LocationInfo { get { return locationInfoCore; } }
        public void MoveTo(MoveToCommandArgs args) {
            LocationInfo.Update(args.Locations[0]);
            if(args.Locations.Length > 1) {
                int length = args.Locations.Length - 1;
                Location[] locations = new Location[length];
                Array.Copy(args.Locations, 1, locations, 0, length);
                LineTo(new MoveToCommandArgs(args.CommandType, locations));
            }
        }
        public void LineTo(MoveToCommandArgs args) {
            PointF[] points = new PointF[args.Locations.Length + 1];
            points[0] = LocationInfo.GetActualLocation(args.UseRelativeCoordinates);
            args.Locations.CopyTo(points, 1);
            Path.AddLines(points);
        }
        void SmoothQuadraticBezierCurveToCore(MoveToCommandArgs args) {

        }
        void SmoothCubicBezierCurveToCore(MoveToCommandArgs args) {//
            Location[] locations = new Location[args.Locations.Length + 1];
            Location secondCurveControlLocation;
            if(args.PathLocationInfo.HasSavedLastCurveLocations(PathLocationInfo.CurveType.Cubic)) {
                secondCurveControlLocation = args.PathLocationInfo.CalcCurveControlPoint(args.Locations[args.Locations.Length - 1], args.Locations[args.Locations.Length - 2]);
            }
            else {
                PointF actualLocation = args.PathLocationInfo.GetActualLocation(args.UseRelativeCoordinates);
                secondCurveControlLocation = new Location(actualLocation, args.CommandType, args.PathLocationInfo, args.UseRelativeCoordinates);
            }
            Array.ConstrainedCopy(args.Locations, 0, locations, 0, args.Locations.Length - 1);
            locations[args.Locations.Length - 1] = secondCurveControlLocation;
            locations[args.Locations.Length] = args.Locations[args.Locations.Length - 1];
            MoveToCommandArgs newArgs = new MoveToCommandArgs(args.CommandType, locations);
            CubicBezierCurveToCore(newArgs);
        }
        protected virtual void CubicBezierCurveToCore(MoveToCommandArgs args) {
            PointF[] points = new PointF[args.Locations.Length + 1];
            points[0] = LocationInfo.GetActualLocation(args.UseRelativeCoordinates);
            args.Locations.CopyTo(points, 1);
            Path.AddBeziers(points);
        }
        protected virtual void QuadraticBezierCurveToCore(MoveToCommandArgs args) {
            PointF[] points = new PointF[args.Locations.Length + 2];
            points[0] = LocationInfo.GetActualLocation(args.UseRelativeCoordinates);
            args.Locations.CopyTo(points, 1);
            points[points.Length - 1] = points[points.Length - 2];
            Path.AddBeziers(points);
        }
        public void CurveTo(MoveToCommandArgs args) {
            int nLocations;
            Action<MoveToCommandArgs> curveRenderMethod = GetCurveRenderMethod(args, out nLocations);
            for(int i = 0; i < args.Locations.Length / nLocations; i++) {
                var locations = new Location[nLocations];
                Array.Copy(args.Locations, i * nLocations, locations, 0, nLocations);
                MoveToCommandArgs newArgs = new MoveToCommandArgs(args.CommandType, locations, args.PathLocationInfo);
                curveRenderMethod(newArgs);
            }
        }
        
        Action<MoveToCommandArgs> GetCurveRenderMethod(MoveToCommandArgs args, out int nLocations) {
            switch(args.CommandType) {
                case PathCommandType.CurveTo:
                    nLocations = 3;
                    return CubicBezierCurveToCore;
                case PathCommandType.SmoothCurveTo:
                    nLocations = 2;
                    return SmoothCubicBezierCurveToCore;
                case PathCommandType.QuadraticCurveTo:
                    nLocations = 2;
                    return QuadraticBezierCurveToCore;
                case PathCommandType.SmoothQuadraticCurveTo:
                    nLocations = 1;
                    return SmoothQuadraticBezierCurveToCore;
                default:
                    nLocations = 0;
                    return x => { };
            }
        }
        public void Draw(Graphics g) {
            Appearance.DrawPath(g, Path);
        }
    }

    public class RenderingEngine {
        List<RenderedPathInfo> renderedPathInfosCore;
        public IReadOnlyList<RenderedPathInfo> RenderedPathInfos { get { return renderedPathInfosCore; } }
        public void Render(IRenderTargetProvider provider) {
            renderedPathInfosCore = new List<RenderedPathInfo>();
            foreach(var path in provider.Paths) {
                RenderedPathInfo renderedPathInfo = RenderPath(path);
                renderedPathInfosCore.Add(renderedPathInfo);
            }
        }
        protected virtual RenderedPathInfo RenderPath(Path path) {
            RenderedPathInfo renderedPathInfo = new RenderedPathInfo(path.PathAppearance, path.LocationInfo);
            foreach(var command in path.PathCommands) {
                RenderPathCommand(renderedPathInfo, command);
            }
            return renderedPathInfo;
        }
        protected virtual void RenderPathCommand(RenderedPathInfo renderedPathInfo, PathCommand command) {
            switch(command.Type) {
                case PathCommandType.MoveTo: renderedPathInfo.MoveTo(command.Args as MoveToCommandArgs); break;
                case PathCommandType.LineTo: renderedPathInfo.LineTo(command.Args as MoveToCommandArgs); break;
                case PathCommandType.CurveTo:
                case PathCommandType.QuadraticCurveTo:
                case PathCommandType.SmoothCurveTo:
                case PathCommandType.SmoothQuadraticCurveTo: renderedPathInfo.CurveTo(command.Args as MoveToCommandArgs); break;
            }
        }
        public void Draw(Graphics g) {
            foreach(var path in RenderedPathInfos) {
                path.Draw(g);
            }
        }
    }
}