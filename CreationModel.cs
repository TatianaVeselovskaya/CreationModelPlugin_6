using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;

namespace CreationModelPlugin_6
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class CopyGroup : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Level level1, level2;
            TakeLevels(doc, out level1, out level2);
            CreateWalls(doc, level1, level2);

            return Result.Succeeded;
        }

        // метод получения уровней
        private static void TakeLevels(Document doc, out Level level1, out Level level2)
        {
            List<Level> listLevel = new FilteredElementCollector(doc)
               .OfClass(typeof(Level))
               .OfType<Level>()
               .ToList();

            level1 = listLevel
                .Where(x => x.Name.Equals("Уровень 1"))
                .FirstOrDefault();
            level2 = listLevel
                .Where(x => x.Name.Equals("Уровень 2"))
                .FirstOrDefault();
        }

        private static void CreateWalls(Document doc, Level level1, Level level2)
        {
            // перевод из футов в мм, внутрення конвертация
            double width = UnitUtils.ConvertToInternalUnits(10000, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(5000, UnitTypeId.Millimeters);
            double dx = width / 2;
            double dy = depth / 2;

            // коллекцию с точками, за счет смещения
            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dx, -dy, 0));
            points.Add(new XYZ(dx, -dy, 0));
            points.Add(new XYZ(dx, dy, 0));
            points.Add(new XYZ(-dx, dy, 0));
            points.Add(new XYZ(-dx, -dy, 0));

            // массив в который будут добавлятьсозданные стены
            List<Wall> walls = new List<Wall>();

            // транцакция внутри которой цикл будет создавать стены
            Transaction transaction = new Transaction(doc, "Построение стен");
            transaction.Start();
            for (int i = 0; i < 4; i++)
            {
                Line line = Line.CreateBound(points[i], points[i + 1]);
                Wall wall = Wall.Create(doc, line, level1.Id, false);
                walls.Add(wall);
                wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(level2.Id);//привязка верхнего уровня стены
            }

            AddDoor(doc, level1, walls[0]);

            CreateWindow(doc, level1, walls[1]);
            CreateWindow(doc, level1, walls[2]);
            CreateWindow(doc, level1, walls[3]);

            AddRoof(doc, level2, walls, width, depth);

            transaction.Commit();
        }
        //методы для создания крыши
        private static void AddRoof(Document doc, Level level2, List<Wall> walls, double width, double depth)
        {
            RoofType roofType = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .OfType<RoofType>()
                .Where(x => x.Name.Equals("Типовой - 400мм"))
                .Where(x => x.FamilyName.Equals("Базовая крыша"))
                .FirstOrDefault(); // получаем один экземпляр

            // 1 способ - по видео

            //    double wallWidth = walls[0].Width;
            //    double dt = wallWidth / 2;
            //    List<XYZ> points = new List<XYZ>();
            //    points.Add(new XYZ(-dt, -dt, 0));
            //    points.Add(new XYZ(dt, -dt, 0));
            //    points.Add(new XYZ(dt, dt, 0));
            //    points.Add(new XYZ(-dt, dt, 0));
            //    points.Add(new XYZ(-dt, -dt, 0));

            //    Application application = doc.Application; // отпечаток - граница дома
            //    CurveArray footprint = application.Create.NewCurveArray();
            //    for (int i = 0; i < 4; i++)// перебор стен
            //    {
            //        LocationCurve curve = walls[i].Location as LocationCurve;
            //        XYZ p1 = curve.Curve.GetEndPoint(0);// линия со смещением
            //        XYZ p2 = curve.Curve.GetEndPoint(1);
            //        Line line = Line.CreateBound(p1 + points[i], p2 + points[i + 1]);
            //        footprint.Append(line);
            //    }
            //    ModelCurveArray footPrintToModelCurveMapping = new ModelCurveArray();
            //    FootPrintRoof footprintRoof = doc.Create.NewFootPrintRoof(footprint, level2, roofType, out footPrintToModelCurveMapping);

            //    foreach (ModelCurve m in footPrintToModelCurveMapping) // наклон крыши
            //    {
            //        footprintRoof.set_DefinesSlope(m, true);
            //        footprintRoof.set_SlopeAngle(m, 0.5);
            //    }


            // 2 способ
            View view = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .OfType<View>()
                .Where(x => x.Name.Equals("Уровень 1"))
                .FirstOrDefault();

            double wallWight = walls[0].Width; // ширина стены
            double dt = wallWight / 2;

            double extrusionStart = -width / 2 - dt; // смещение
            double extrusionEnd = width / 2 + dt;
            double curveStart = -depth / 2 - dt;
            double curveEnd = +depth / 2 + dt;

            CurveArray curveArray = new CurveArray();           //отпечаток границы дома
            curveArray.Append(Line.CreateBound(new XYZ(0, curveStart, level2.Elevation), new XYZ(0, 0, level2.Elevation + 10)));
            curveArray.Append(Line.CreateBound(new XYZ(0, 0, level2.Elevation + 10), new XYZ(0, curveEnd, level2.Elevation)));

            ReferencePlane plane = doc.Create.NewReferencePlane(new XYZ(0, 0, 0), new XYZ(0, 0, 20), new XYZ(0, 20, 0), view);
            ExtrusionRoof extrusionRoof = doc.Create.NewExtrusionRoof(curveArray, plane, level2, roofType, extrusionStart, extrusionEnd);
            extrusionRoof.EaveCuts = EaveCutterType.TwoCutSquare;

        }

        // метод для создания двери 
        private static void AddDoor(Document doc, Level level1, Wall wall)
        {
            FamilySymbol doorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfType<FamilySymbol>() // фильтрацию по типу, изменение коллекции
                .Where(x => x.Name.Equals("0915 x 2134 мм"))//
                .Where(x => x.FamilyName.Equals("Одиночные-Щитовые"))
                .FirstOrDefault();

            // точка в которую добавляем дверь
            LocationCurve hostCurve = wall.Location as LocationCurve; //стена - отрезок/кривая
            XYZ point1 = hostCurve.Curve.GetEndPoint(0);
            XYZ point2 = hostCurve.Curve.GetEndPoint(1);
            XYZ point = (point1 + point2) / 2; // точка для установки двери

            if (!doorType.IsActive)
                doorType.Activate();

            doc.Create.NewFamilyInstance(point, doorType, wall, level1, StructuralType.NonStructural);
        }
        // метод для создания окон
        private static void CreateWindow(Document doc, Level level1, Wall wall)
        {
            FamilySymbol winType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0915 x 1830 мм"))
                .Where(x => x.FamilyName.Equals("Фиксированные"))
                .FirstOrDefault();

            LocationCurve hostCurve = wall.Location as LocationCurve;
            XYZ point1 = hostCurve.Curve.GetEndPoint(0);
            XYZ point2 = hostCurve.Curve.GetEndPoint(1);
            XYZ point = (point1 + point2) / 2;

            if (!winType.IsActive)
                winType.Activate();

            var window = doc.Create.NewFamilyInstance(point, winType, wall, level1, StructuralType.NonStructural);
            Parameter sillHeight = window.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
            double sh = UnitUtils.ConvertToInternalUnits(900, UnitTypeId.Millimeters);
            sillHeight.Set(sh);
        }
    }
}
