﻿using System;
using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using Rhino.Display;
using System.IO;

namespace patternTest
{
    class PartitionMakerCorner
    {
        public static DivMakerOutput GetCorner(PartitionParam param, double targetArea)
        {
            //나중에 좀 더 일반화할 수 있을 듯
            List<RoomLine> coreSeg = param.OutlineLabel.Core;
            int originIndex = coreSeg.FindIndex(i => i.PureLine == param.OriginPost.BasePureLine);
            int preIndex = originIndex - 1;
            Point3d origin = param.OriginPost.Point;
            Polyline outline = param.OutlineLabel.Pure;

            Line cornerLinePre = PCXTools.ExtendFromPt(origin, outline, coreSeg[preIndex].UnitNormal);
            Line cornerLinePost = PCXTools.ExtendFromPt(origin, outline, coreSeg[originIndex].UnitNormal);

            double outlineParamPre = outline.ClosestParameter(cornerLinePre.PointAt(1));
            double outlineParamPost = outline.ClosestParameter(cornerLinePost.PointAt(1));

            double paramPreFloor = Math.Floor(outlineParamPre);
            double paramPostCeiling = Math.Ceiling(outlineParamPost);


            //vertexSetting
            //dividerDrawer랑 같은 코드...
            List<Point3d> partitionVertex = new List<Point3d>();
            partitionVertex.Add(outline.PointAt(outlineParamPost));

            if (outlineParamPre < outlineParamPost) //인덱스 꼬였을 때
            {
                if (outlineParamPost != paramPostCeiling)
                    partitionVertex.Add(outline.PointAt(paramPostCeiling));

                double paramLast = outline.Count - 1;

                for (double i = paramPostCeiling + 1; i < paramLast; i++)
                    partitionVertex.Add(outline.PointAt(i));

                for (double i = 0; i < paramPreFloor; i++)
                    partitionVertex.Add(outline.PointAt(i));

                if (outlineParamPre != paramPreFloor)
                    partitionVertex.Add(outline.PointAt(paramPreFloor));

            }

            else
            {


                double betweenIndexCounter = paramPreFloor - paramPostCeiling;

                if (betweenIndexCounter == 0)
                {
                    if ((outlineParamPost != paramPostCeiling) && (outlineParamPre != paramPreFloor))
                        partitionVertex.Add(outline.PointAt(paramPostCeiling));
                }

                else if (betweenIndexCounter > 0)
                {
                    if (outlineParamPost != paramPostCeiling)
                        partitionVertex.Add(outline.PointAt(paramPostCeiling));

                    for (double i = paramPostCeiling + 1; i < paramPreFloor; i++)
                        partitionVertex.Add(outline.PointAt(i));

                    if (outlineParamPre != paramPreFloor)
                        partitionVertex.Add(outline.PointAt(paramPreFloor));
                }
            }

            partitionVertex.Add(outline.PointAt(outlineParamPre));

            //decider
            //확장성 있게 만들 수 있나?
            int vertextCount = partitionVertex.Count;

            if (vertextCount == 2)
                return DrawAtNoFoldOutline(param, targetArea, partitionVertex);

            if (vertextCount == 3)
                return DrawAtOneFoldOutline(param, targetArea, partitionVertex);

            return DrawAtMultiFoldOutline(param, targetArea, partitionVertex);
        }

        //중복코드 나중에 지워보자..
        private static DivMakerOutput DrawAtNoFoldOutline(PartitionParam param, double targetArea, List<Point3d> outlineVertex)
        {
            int iterNum = 10;
            int breaker = 0;

            Line mainLine = GetMainLine(param, outlineVertex);     
            Vector3d mainAlign = mainLine.UnitTangent;
            Vector3d mainPerp = VectorTools.RotateVectorXY(mainAlign, Math.PI / 2);
            Point3d origin = param.OriginPost.Point;

            double dotProduct = Vector3d.Multiply(mainAlign, param.OriginPost.BaseLine.UnitNormal);
            double dotTolerance = 0.005;

            bool isMainDirecPreDiv = false; 

            if (Math.Abs(dotProduct) < dotTolerance)
            {
                int originIndex = param.OutlineLabel.Core.FindIndex
                    (i => i.PureLine == param.OriginPost.BasePureLine);

                param.OriginPost = new PartitionOrigin(origin, param.OutlineLabel.Core[originIndex-1]);
                mainPerp = VectorTools.RotateVectorXY(mainAlign, -Math.PI / 2);
                isMainDirecPreDiv = true;
            }

            
            double lowerBound = 0;
            double upperBound = mainLine.Length;
            Polyline polyOutput = new Polyline();

            while (lowerBound < upperBound)
            {
                if (breaker > iterNum)
                    break;

                double tempStatus = (upperBound - lowerBound) / 2 + lowerBound;

                Point3d tempAnchor = origin + mainAlign * tempStatus;
                if (isMainDirecPreDiv)
                    tempAnchor = origin + mainAlign * (mainLine.Length - tempStatus);

                List<RoomLine> cornerPartitions = new List<RoomLine>();
                cornerPartitions.Add(new RoomLine(new Line(origin, tempAnchor), LineType.Inner));
                Line anchorToOutline = PCXTools.ExtendFromPt(tempAnchor, param.OutlineLabel.Pure, mainPerp);
                cornerPartitions.Add(new RoomLine(anchorToOutline, LineType.Inner));

                Partition tempPartition = new Partition(cornerPartitions,param.OriginPost);
                param.PartitionPost = tempPartition;
                Polyline tempPoly = RoomOutlineDrawer.GetRoomOutline(param);
                polyOutput = tempPoly;

                double tempArea = PolylineTools.GetArea(tempPoly);

                if (targetArea > tempArea)
                    lowerBound = tempStatus;
                else if (targetArea < tempArea)
                    upperBound = tempStatus;
                else
                {
                    lowerBound = tempArea;
                    upperBound = tempArea;
                }

                breaker++;
            }

            return new DivMakerOutput(polyOutput, param);
        }

        private static DivMakerOutput DrawAtOneFoldOutline(PartitionParam param, double targetArea, List<Point3d> outlineVertex)
        {
            double dotTolerance = 0.005;

            Vector3d outlineVector1 = new Vector3d(outlineVertex[0] - outlineVertex[1]);
            Vector3d outlineVector2 = new Vector3d(outlineVertex[2] - outlineVertex[1]);

            double dotProduct = Math.Abs(Vector3d.Multiply(outlineVector1, outlineVector2));

            if (dotProduct < dotTolerance)
                return DrawAtNoFoldOutline(param, targetArea, outlineVertex);

            return DrawAtMultiFoldOutline(param, targetArea, outlineVertex);                                    
        }

        private static DivMakerOutput DrawAtMultiFoldOutline(PartitionParam param, double targetArea, List<Point3d> outlineVertex)
        {
            Line mainLine = GetMainLine(param, outlineVertex);
            List<Point3d> canMakePerpVertex = new List<Point3d>();

            Vector3d mainAlign = mainLine.UnitTangent;
            Vector3d mainPerp = VectorTools.RotateVectorXY(mainAlign, Math.PI / 2);
            Point3d origin = param.OriginPost.Point;

            if (mainAlign != param.OriginPost.BaseLine.UnitNormal)
            {
                int originIndex = param.OutlineLabel.Core.FindIndex
                    (i => i.PureLine == param.OriginPost.BasePureLine);

                param.OriginPost = new PartitionOrigin(origin, param.OutlineLabel.Core[originIndex - 1]);
                mainPerp = VectorTools.RotateVectorXY(mainAlign, -Math.PI / 2);
            }

            int lastVertexIndex = outlineVertex.Count - 1;

            for (int i = 1; i < lastVertexIndex; i++)
            {
                Vector3d toPreVector = new Vector3d(outlineVertex[i - 1] - outlineVertex[i]);
                Vector3d toPostVector = new Vector3d(outlineVertex[i + 1] - outlineVertex[i]);
                Vector3d toMainVector = -mainPerp;

                if (IsBetweenVector(toPreVector, toPostVector, toMainVector))
                    canMakePerpVertex.Add(outlineVertex[i]);
                
            }

            //SeivePerpVertex



            //DrawAtEachVertex
            List<DivMakerOutput> outputCandidate = new List<DivMakerOutput>();
            
            foreach (Point3d i in canMakePerpVertex)
            {
                Line vertexToMainLine = new Line(i, i - mainPerp * 1);
                Point3d tempAnchor = CCXTools.GetCrossPt(mainLine, vertexToMainLine);

                List<RoomLine> tempPartition = new List<RoomLine>();
                tempPartition.Add(new RoomLine(new Line(origin, tempAnchor), LineType.Inner));
                tempPartition.Add(new RoomLine(new Line(tempAnchor, i), LineType.Inner));

                PartitionParam tempParam = new PartitionParam(param);
                tempParam.PartitionPost = new Partition(tempPartition, param.OriginPost);
                Polyline tempPoly = RoomOutlineDrawer.GetRoomOutline(param);

                outputCandidate.Add(new DivMakerOutput(tempPoly, tempParam));
            }

            outputCandidate.Add(PartitionMaker.DrawOrtho(param));

            //TestCandidate
            //나중에 수정.. 지금은 면적일치정도로만..
            outputCandidate.Sort((x, y) =>
            (Math.Abs(targetArea - PolylineTools.GetArea(x.Poly)).CompareTo(Math.Abs(targetArea - PolylineTools.GetArea(y.Poly)))));

            return outputCandidate[0];
        }


        /// <summary>
        /// Cross(pre, Post) // -ZAxis 기준으로 현재 벡터가 두 벡터 사이에 있는지를 판별합니다.
        /// </summary>
        private static Boolean IsBetweenVector(Vector3d preVector, Vector3d postVector, Vector3d testVector)
        {
            double crossTolerance = 0.005;

            Vector3d toPreCross = Vector3d.CrossProduct(testVector, preVector);
            Vector3d toPostCross = Vector3d.CrossProduct(testVector, postVector);
            Vector3d preToPostCross = Vector3d.CrossProduct(preVector, postVector);

            if (Math.Abs(toPreCross.Length) < crossTolerance || Math.Abs(toPostCross.Length) < crossTolerance)
                return false;

            bool IsToPostNegZAlign = Vector3d.Multiply(toPostCross, -Vector3d.ZAxis) > 0;
            bool IsToPreZAlign = Vector3d.Multiply(toPreCross, Vector3d.ZAxis) > 0;
            bool IsPreToPostZAlign = Vector3d.Multiply(preToPostCross, Vector3d.ZAxis) > 0;

            if (IsToPreZAlign && IsToPostNegZAlign)
                return true;

            if (IsToPreZAlign && !IsToPostNegZAlign)
            {
                if (IsPreToPostZAlign)
                    return true;
                return false;
            }

            if (!IsToPreZAlign && IsToPostNegZAlign)
            {
                if (IsPreToPostZAlign)
                    return true;
                return false;
            }

            return false;
        }

        private static Line GetMainLine(PartitionParam param, List<Point3d> outlineVertex)
        {
            Point3d noFoldOrigin = param.OriginPost.Point;
            Line cornerLinePre = new Line(noFoldOrigin, outlineVertex.Last());
            Line cornerLinePost = new Line(noFoldOrigin, outlineVertex.First());

            if (cornerLinePre.Length > cornerLinePost.Length)
                return cornerLinePre;
            else
                return cornerLinePost;
        }
    }
}