/*************************************************************************
 *  Copyright (C) 2024 -2099 URS. All rights reserved.
 *------------------------------------------------------------------------
 *  File         :  UnityNDrawr.cs
 *  Description  :  Null.
 *------------------------------------------------------------------------
 *  Author       :  SGD
 *  Version      :  1.0.0
 *  Date         :  2024/9/6
 *  Description  :  Initial development version.
 *************************************************************************/
//#define NDRAW_UPDATE_IN_COROUTINE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NDraw
{
    [RequireComponent(typeof(Camera))]
    public class UnityNDrawer : MonoBehaviour
    {
        static UnityNDrawer e;

        static Material material;
        new Camera camera;

        public static bool Exists { get { return e != null && e.enabled; } }

        static readonly Vector2 one = Vector2.one;

        private void Awake() {
            e = this;
        }

        protected virtual void Start() {
            if (material == null)
                CreateLineMaterial();

            camera = GetComponent<Camera>();

#if NDRAW_UPDATE_IN_COROUTINE
            StartCoroutine(PostRender());
#endif
        }

        private void OnDestroy() {
            NDrawHelper.Clear();
        }

        WaitForEndOfFrame wof = new WaitForEndOfFrame();

#if !NDRAW_UPDATE_IN_COROUTINE
        private void OnPostRender() {
            if (enabled)
                Render();

            NDrawHelper.Clear();
        }

#endif

#if NDRAW_UPDATE_IN_COROUTINE
        IEnumerator PostRender()
        {
            while (true)
            {
                yield return wof;

                if (enabled)
                    Render();

                NDrawHelper.Clear();
            }
        }
#endif

        void CreateLineMaterial() {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            material = new Material(shader);
            //material.hideFlags = HideFlags.HideAndDontSave;
            // Turn on alpha blending
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // Turn backface culling off
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            // Turn off depth writes
            material.SetInt("_ZWrite", 0);

            // makes the material draw on top of everything
            material.SetInt("_ZTest", 0);
        }

        protected void Render() {
            material.SetPass(0);

            //-------------
            // WORLD SPACE
            //-------------

            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;

            GL.Begin(GL.LINES);
            GL.Color(Color.white);
            ProcessPoints(NDrawHelper.worldPoints, NDrawHelper.worldColorIndices, false);
            GL.End();

            GL.PopMatrix();

            //--------------
            // SCREEN SPACE
            //--------------

            GL.PushMatrix();

#if NDRAW_ORHTO_MULTIPLICATION
            GL.LoadOrtho();
#else
            GL.LoadPixelMatrix();
#endif

            GL.Begin(GL.TRIANGLES);
            ProcessPoints(NDrawHelper.screenTrisPoints, NDrawHelper.screenTrisColorIndices, true);
            GL.End();

            GL.Begin(GL.LINES);
            ProcessPoints(NDrawHelper.screenPoints, NDrawHelper.screenColorIndices, true);
            GL.End();

            GL.PopMatrix();

        }

        static void ProcessPoints(List<Vector3> points, List<NDrawHelper.ColorIndex> colorIndices, bool screen) {
            if (points.Count == 0) return;

            //GL.Color(Color.white);
#if NDRAW_ORHTO_MULTIPLICATION
            Vector2 s = screen ? new Vector2(1.0f / Screen.width, 1.0f / Screen.height) : one;
#endif

            bool hasColors = colorIndices.Count > 0;

            int ci = 0;
            int ct = points.Count;
            for (int i = 0; i < ct; i++) {
                // handle color
                if (hasColors && colorIndices[ci].i == i) {
                    GL.Color(colorIndices[ci].c);

                    ci++;
                    if (ci >= colorIndices.Count) ci = 0;
                }

                // push vertex
#if NDRAW_ORHTO_MULTIPLICATION
                if (screen)
                    GL.Vertex(points[i] * s);
                else
                    GL.Vertex(points[i]);
#else
                GL.Vertex(points[i]);
#endif
            }
        }

        protected void ClearLines() {
            NDrawHelper.Clear();
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class World
        {
            /// <summary>
            ///画一个圆锥形截面，在向前的方向与近视点。
            ///根据偏心度绘制圆形、椭圆形、抛物线或双曲线。
            ///它没有画出双曲线的负面部分。
            /// Draw a conic section, oriented with periapsis at forward.
            /// Draws a circle, ellipse, parabola or hyperbola depending on the eccentricity.
            /// It does not draw the negative part of hyperbola.
            /// </summary>
            /// <param name="center">The focus point</param>
            /// <param name="eccentricity">e=0 is circle, e less than 1 is ellipse, e=1 is parabola, e more than 1 is hyperbola </param>
            /// <param name="semiMajorAxis">Semi major axis of the ellipse, in case e is more than 1 it should be negative</param>
            /// <param name="normal">The normal vector of the section</param>
            /// <param name="periapsisDirection">The direction of the periapsis</param>
            public static void ConicSection(Vector3 center, float eccentricity, float semiMajorAxis, Vector3 normal, Vector3 periapsisDirection, int interpolations) {
                float semilatus = eccentricity == 1 ? semiMajorAxis :
                    semiMajorAxis * (1 - eccentricity * eccentricity);

                if (semilatus <= 0) return;
                if (interpolations <= 0) interpolations = 10;
                if (eccentricity < 0) eccentricity = 0;

                periapsisDirection = Vector3.ProjectOnPlane(periapsisDirection, normal).normalized;
                Vector3 right = Vector3.Cross(periapsisDirection, normal).normalized;

                Vector3 prevlp = new Vector3();
                Vector3 prevrp = new Vector3();

                int num = interpolations;
                float thetadiff = Mathf.PI / num;
                float theta = 0;

                bool breakn = false;

                for (int i = 0; i < num + 1; i++) {
                    float cosTheta = Mathf.Cos(theta);
                    float r = semilatus / (1 + eccentricity * cosTheta);

                    if (r < 0) { r *= -100; breakn = true; }

                    Vector3 rvec = right * Mathf.Sin(theta) * r;
                    Vector3 fvec = periapsisDirection * cosTheta * r;

                    // Left side
                    Vector3 lp = center - rvec + fvec;

                    if (theta != 0) worldPoints.Add(prevlp);
                    worldPoints.Add(lp);
                    prevlp = lp;

                    // Right side
                    Vector3 rp = center + rvec + fvec;

                    if (theta != 0) worldPoints.Add(prevrp);
                    worldPoints.Add(rp);
                    prevrp = rp;

                    theta += thetadiff;

                    if (breakn) break; // Prevents drawing the negative part of hyperbola
                }
            }

            /// <summary>
            /// 使用近点和远点绘制世界空间中的椭圆轨道
            /// Draws an elliptic orbit in world space using periapsis and apoapsis
            /// </summary>
            public static void ConicSectionUsingApses(Vector3 center, float periapsis, float apoapsis, Vector3 normal, Vector3 forward, int interpolations) {
                float a = (periapsis + apoapsis) / 2;
                float e = (apoapsis - periapsis) / (apoapsis + periapsis);

                // TODO: Add interpolations
                ConicSection(center, e, a, normal, forward, interpolations);
            }
        }
    }
    public static partial class NDrawHelper
    {
#if NET46
        internal readonly struct ColorIndex
#else
        internal struct ColorIndex
#endif
        {
            public readonly int i;
            public readonly Color c;

            public ColorIndex(int i, Color c) {
                this.i = i;
                this.c = c;
            }
        }

        internal static List<Vector3> screenPoints = new List<Vector3>();
        internal static List<Vector3> worldPoints = new List<Vector3>();
        internal static List<Vector3> screenTrisPoints = new List<Vector3>();

        internal static List<ColorIndex> screenColorIndices = new List<ColorIndex>();
        internal static List<ColorIndex> worldColorIndices = new List<ColorIndex>();
        internal static List<ColorIndex> screenTrisColorIndices = new List<ColorIndex>();

        internal static void Clear() {
            screenPoints.Clear();
            screenColorIndices.Clear();

            worldPoints.Clear();
            worldColorIndices.Clear();

            screenTrisPoints.Clear();
            screenTrisColorIndices.Clear();
        }

        public static partial class Screen
        {
            public static void SetColor(Color color) {
                if (!UnityNDrawer.Exists) return;

                int pointIndex = screenPoints.Count;
                int lastci = screenColorIndices.Count - 1;

                ColorIndex ci = new ColorIndex(pointIndex, color);

                // Overwrite if last index is the same as this one
                if (screenColorIndices.Count > 0 &&
                    screenColorIndices[lastci].i == pointIndex) {
                    screenColorIndices[lastci] = ci;
                    return;
                }

                screenColorIndices.Add(ci);
            }

            public static void SetFillColor(Color color) {
                if (!UnityNDrawer.Exists) return;

                int pointIndex = screenTrisPoints.Count;
                int lastci = screenTrisColorIndices.Count - 1;

                ColorIndex ci = new ColorIndex(screenTrisPoints.Count, color);

                // Overwrite if last index is the same as this one
                if (screenTrisColorIndices.Count > 0 &&
                    screenTrisColorIndices[lastci].i == pointIndex) {
                    screenTrisColorIndices[lastci] = ci;
                    return;
                }

                screenTrisColorIndices.Add(ci);
            }
        }

        public static partial class World
        {
            public static void SetColor(Color color) {
                if (!UnityNDrawer.Exists) return;

                ColorIndex ci = new ColorIndex(worldPoints.Count, color);
                worldColorIndices.Add(ci);
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            /// <summary>
            ///屏幕函数图形-警告: 由于 lambda 函数而产生 GC 分配。
            /// Graphs a function on screen
            /// Warning: Produces GC allocs due to the lambda function.
            /// </summary>
            /// <param name="graphRect">Rect on screen</param>
            /// <param name="unitRect">A rect that defines a size of a unit of graph compared to pixels.
            /// E.g. a 0,0,1,1 unitRect will have it's lower left corner at 0,0 with unit size of 1 pixel. </param>
            /// <param name="func">A function that takes an 'x' and returns a 'y'</param>
            public static void Graph(Rect graphRect, Rect unitRect, System.Func<float, float> func) {
                if (!UnityNDrawer.Exists) return;

                float prev = 0;
                for (int i = 0; i < graphRect.width; i++) {
                    float input = -unitRect.x / unitRect.width + (i / unitRect.width); // z + 
                    float v0 = unitRect.y + func.Invoke(input) * unitRect.height;

                    v0 = Mathf.Clamp(v0,
                        0,
                        graphRect.height);

                    int screeny1 = (int)(graphRect.y + v0);
                    int screeny2 = (int)(graphRect.y + prev);

                    Line(
                        (int)graphRect.x + i, screeny1,
                        (int)graphRect.x + i + 1, screeny2);

                    prev = v0;
                }
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            public static void Line(int p1x, int p1y, int p2x, int p2y) {
                if (!UnityNDrawer.Exists) return;

                screenPoints.Add(new Vector2(p1x, p1y));
                screenPoints.Add(new Vector2(p2x, p2y));
            }

            public static void Line(Vector2 p1, Vector2 p2) {
                if (!UnityNDrawer.Exists) return;

                screenPoints.Add(p1);
                screenPoints.Add(p2);
            }

            public static void MultiLine(Vector2[] points) {
                if (!UnityNDrawer.Exists) return;

                if (points.Length < 2) return;

                for (int i = 0; i < points.Length - 1; i++) {
                    screenPoints.Add(points[i]);
                    screenPoints.Add(points[i + 1]);
                }
            }

            public static void MultiLine(List<Vector2> points) {
                if (!UnityNDrawer.Exists) return;

                int pct = points.Count;
                if (pct < 2) return;

                for (int i = 0; i < pct - 1; i++) {
                    screenPoints.Add(points[i]);
                    screenPoints.Add(points[i + 1]);
                }
            }

            public static void MultiLine(Vector2[] points, Vector2 offset, float scale) {
                if (!UnityNDrawer.Exists) return;

                if (points.Length < 2) return;

                for (int i = 0; i < points.Length - 1; i++) {
                    screenPoints.Add(offset + points[i] * scale);
                    screenPoints.Add(offset + points[i + 1] * scale);
                }
            }
        }

        public static partial class World
        {
            public static void Line(Vector3 p1, Vector3 p2) {
                if (!UnityNDrawer.Exists) return;

                worldPoints.Add(p1);
                worldPoints.Add(p2);
            }

            public static void Ray(Vector3 point, Vector3 dir) {
                if (!UnityNDrawer.Exists) return;

                worldPoints.Add(point);
                worldPoints.Add(point + dir);
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            public static void Mesh(int x, int y, Mesh mesh, Material material) {
                if (!UnityNDrawer.Exists) return;
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            /// <summary>
            ///  饼图
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <param name="innerRadius"></param>
            /// <param name="outerRadius"></param>
            /// <param name="value"></param>
            public static void Pie(int x, int y, float innerRadius, float outerRadius, float value) {
                if (!UnityNDrawer.Exists) return;

                Vector2 ci_in = new Vector2(x, y + innerRadius);
                Vector2 ci_out = new Vector2(x, y - outerRadius);

                Vector2 ci0_in = ci_in;
                Vector2 ci0_out = ci_out;

                Vector2 cil_in = ci_in;
                Vector2 cil_out = ci_out;

                float s = Mathf.Sign(value);
                float add = 0.3f * s;
                float limit = Mathf.Abs(2 * Mathf.PI * value);

                for (float theta = 0.0f; Mathf.Abs(theta) < limit; theta += add) {

                    ci_in = new Vector2(
                        x + (Mathf.Sin(theta) * innerRadius),
                        y - (Mathf.Cos(theta) * innerRadius));

                    ci_out = new Vector2(
                        x + (Mathf.Sin(theta) * outerRadius),
                        y - (Mathf.Cos(theta) * outerRadius));

                    screenTrisPoints.Add(cil_in);
                    screenTrisPoints.Add(cil_out);
                    screenTrisPoints.Add(ci_out);

                    screenTrisPoints.Add(ci_out);
                    screenTrisPoints.Add(ci_in);
                    screenTrisPoints.Add(cil_in);

                    // previous points
                    cil_in = ci_in;
                    cil_out = ci_out;
                }



                // last segment
                ci_in = new Vector2(
                    x + (Mathf.Sin(limit * s) * innerRadius),
                    y - (Mathf.Cos(limit * s) * innerRadius));

                ci_out = new Vector2(
                    x + (Mathf.Sin(limit * s) * outerRadius),
                    y - (Mathf.Cos(limit * s) * outerRadius));

                screenTrisPoints.Add(cil_in);
                screenTrisPoints.Add(cil_out);
                screenTrisPoints.Add(ci_out);

                screenTrisPoints.Add(ci_out);
                screenTrisPoints.Add(ci_in);
                screenTrisPoints.Add(cil_in);
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            public static void Rect(Rect rect) {
                if (!UnityNDrawer.Exists) return;

                Rect(rect.x, rect.y, rect.width, rect.height);
            }

            public static void Rect(float x, float y, float width, float height) {
                if (!UnityNDrawer.Exists) return;

                screenPoints.Add(new Vector2(x, y));
                screenPoints.Add(new Vector2(x + width, y));

                screenPoints.Add(new Vector2(x + width, y));
                screenPoints.Add(new Vector2(x + width, y + height));

                screenPoints.Add(new Vector2(x + width, y + height));
                screenPoints.Add(new Vector2(x, y + height));

                screenPoints.Add(new Vector2(x, y + height));
                screenPoints.Add(new Vector2(x, y));
            }

            public static void Circle(Vector2 center, float pixelRadius, int interpolations = 40) {
                Circle(center.x, center.y, pixelRadius, interpolations);
            }

            public static void Circle(float centerX, float centerY, float pixelRadius, int interpolations = 40) {
                if (!UnityNDrawer.Exists) return;

                Vector2 size = new Vector2(pixelRadius, pixelRadius);
                Vector2 center = new Vector2(centerX, centerY);

                Ellipse(center, size, interpolations);
            }
            /// <summary>
            /// 画椭圆
            /// </summary>
            /// <param name="center"></param>
            /// <param name="size"></param>
            /// <param name="interpolations"></param>
            public static void Ellipse(Vector2 center, Vector2 size, int interpolations = 40) {
                if (!UnityNDrawer.Exists) return;

                float radX = size.x;
                float radY = size.y;

                Vector2 ci = new Vector2(
                    center.x + (1 * radX),
                    center.y);

                Vector2 ci0 = ci;
                float step = (2 * Mathf.PI) / interpolations;

                for (int i = 0; i < interpolations; i++) {
                    float theta = i * step;
                    screenPoints.Add(ci);

                    ci = new Vector2(center.x + (Mathf.Cos(theta) * radX), center.y + (Mathf.Sin(theta) * radY));

                    screenPoints.Add(ci);
                }

                // close
                screenPoints.Add(ci);
                screenPoints.Add(ci0);
            }
            /// <summary>
            /// 显示屏幕网格网格
            /// </summary>
            /// <param name="xLineNum"></param>
            /// <param name="yLineNum"></param>
            /// <param name="rect"></param>
            public static void Grid(int xLineNum, int yLineNum, Rect rect) {
                if (!UnityNDrawer.Exists) return;

                float add = rect.height / yLineNum;
                for (int i = 0; i <= yLineNum; i++) {
                    float y = rect.yMax - i * add;
                    screenPoints.Add(new Vector2(rect.x, y));
                    screenPoints.Add(new Vector2(rect.xMax, y));
                }

                add = rect.width / yLineNum;
                for (int i = 0; i <= yLineNum; i++) {
                    float x = rect.x + i * add;
                    screenPoints.Add(new Vector2(x, rect.y));
                    screenPoints.Add(new Vector2(x, rect.yMax));
                }
            }

            // FILLED

            public static void FillRect(Rect rect) {
                if (!UnityNDrawer.Exists) return;

                FillRect(rect.x, rect.y, rect.width, rect.height);
            }

            public static void FillRect(float x, float y, float width, float height) {
                if (!UnityNDrawer.Exists) return;

                Vector3 p0 = new Vector3(x, y);
                Vector3 p1 = new Vector3(x + width, y);
                Vector3 p2 = new Vector3(x, y + height);
                Vector3 p3 = new Vector3(x + width, y + height);

                screenTrisPoints.Add(p0);
                screenTrisPoints.Add(p1);
                screenTrisPoints.Add(p2);

                screenTrisPoints.Add(p1);
                screenTrisPoints.Add(p3);
                screenTrisPoints.Add(p2);
            }
            /// <summary>
            /// 填充三角形
            /// </summary>
            /// <param name="p1"></param>
            /// <param name="p2"></param>
            /// <param name="p3"></param>
            public static void FillTriangle(Vector2 p1, Vector2 p2, Vector2 p3) {
                if (!UnityNDrawer.Exists) return;

                screenTrisPoints.Add(p1);
                screenTrisPoints.Add(p2);
                screenTrisPoints.Add(p3);
            }
            /// <summary>
            /// 填充扇形
            /// </summary>
            /// <param name="points"></param>
            public static void FillFanPolygon(Vector2[] points) {
                if (!UnityNDrawer.Exists) return;
                if (points == null || points.Length < 2) return;

                Vector3 p0 = points[0];

                for (int i = 1; i < points.Length - 1; i++) {
                    screenTrisPoints.Add(p0);
                    screenTrisPoints.Add(points[i]);
                    screenTrisPoints.Add(points[i + 1]);
                }
            }
            /// <summary>
            /// 填充扇形
            /// </summary>
            /// <param name="points"></param>
            public static void FillFanPolygon(List<Vector2> points) {
                if (!UnityNDrawer.Exists) return;
                if (points == null || points.Count < 2) return;

                Vector3 p0 = points[0];

                int pct = points.Count;
                for (int i = 1; i < pct - 1; i++) {
                    screenTrisPoints.Add(p0);
                    screenTrisPoints.Add(points[i]);
                    screenTrisPoints.Add(points[i + 1]);
                }
            }
        }

        public static partial class World
        {
            public static void Cube(Vector3 center, Vector3 size, Vector3 forward, Vector3 up) {
                if (!UnityNDrawer.Exists) return;

                forward = forward.normalized;
                up = Vector3.ProjectOnPlane(up, forward).normalized;
                Vector3 right = Vector3.Cross(forward, up);

                Vector3 frw = forward * size.z * 0.5f;
                Vector3 rgt = right * size.x * 0.5f;
                Vector3 upw = up * size.y * 0.5f;

                // vertical lines
                worldPoints.Add(center - frw - rgt - upw);
                worldPoints.Add(center - frw - rgt + upw);

                worldPoints.Add(center - frw + rgt - upw);
                worldPoints.Add(center - frw + rgt + upw);

                worldPoints.Add(center + frw - rgt - upw);
                worldPoints.Add(center + frw - rgt + upw);

                worldPoints.Add(center + frw + rgt - upw);
                worldPoints.Add(center + frw + rgt + upw);

                // horizontal lines
                worldPoints.Add(center - frw - rgt - upw);
                worldPoints.Add(center - frw + rgt - upw);

                worldPoints.Add(center - frw - rgt + upw);
                worldPoints.Add(center - frw + rgt + upw);

                worldPoints.Add(center + frw - rgt - upw);
                worldPoints.Add(center + frw + rgt - upw);

                worldPoints.Add(center + frw - rgt + upw);
                worldPoints.Add(center + frw + rgt + upw);

                // forward lines
                worldPoints.Add(center - frw - rgt - upw);
                worldPoints.Add(center + frw - rgt - upw);

                worldPoints.Add(center - frw + rgt - upw);
                worldPoints.Add(center + frw + rgt - upw);

                worldPoints.Add(center - frw - rgt + upw);
                worldPoints.Add(center + frw - rgt + upw);

                worldPoints.Add(center - frw + rgt + upw);
                worldPoints.Add(center + frw + rgt + upw);
            }

            public static void Circle(Vector3 center, float radius, Vector3 normal, int interpolations = 100) {
                if (!UnityNDrawer.Exists) return;

                normal = normal.normalized;
                Vector3 forward = normal == Vector3.up ?
                    Vector3.ProjectOnPlane(Vector3.forward, normal).normalized :
                    Vector3.ProjectOnPlane(Vector3.up, normal).normalized;

                Vector3 p = center + forward * radius;

                float step = 360.0f / interpolations;

                for (int i = 0; i <= interpolations; i++) {
                    float theta = i * step;

                    worldPoints.Add(p);

                    Vector3 angleDir = Quaternion.AngleAxis(theta, normal) * forward;
                    p = center + angleDir * radius;

                    worldPoints.Add(p);
                }
            }
            /// <summary>
            /// 螺旋线
            /// </summary>
            /// <param name="p1"></param>
            /// <param name="p2"></param>
            /// <param name="forward"></param>
            /// <param name="radius"></param>
            /// <param name="angle"></param>
            public static void Helix(Vector3 p1, Vector3 p2, Vector3 forward, float radius, float angle) {
                if (!UnityNDrawer.Exists) return;

                Vector3 diff = p2 - p1;
                Vector3 normal = diff.normalized;

                forward = Vector3.ProjectOnPlane(forward, normal).normalized;

                //Vector3 right = Vector3.Cross(normal, forward);

                Vector3 ci = p1 + forward * radius;

                float lengthFactor = diff.magnitude;

                for (float f = 0.0f; f <= 1.0f; f += 0.02f) {
                    float theta = f * angle * Mathf.Deg2Rad;

                    //Vector3 ci = center + forward * Mathf.Cos(theta) * radius + right * Mathf.Sin(theta) * radius;

                    worldPoints.Add(ci);

                    Vector3 offset = normal * f * lengthFactor;

                    Vector3 angleDir = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, normal) * forward;
                    ci = p1 + angleDir.normalized * radius + offset;

                    worldPoints.Add(ci);

                    //if (theta != 0)
                    //GL.Vertex(ci);
                }

                //worldPoints.Add(ci);
                //worldPoints.Add(c0);
            }
            /// <summary>
            /// 螺旋线2
            /// </summary>
            /// <param name="position"></param>
            /// <param name="normal"></param>
            /// <param name="forward"></param>
            /// <param name="radius"></param>
            public static void Spiral(Vector3 position, Vector3 normal, Vector3 forward, float radius) {
                const int count = 80;
                const float angle = -17.453f;
                float add = radius / count;

                Vector3 lastP = Vector3.zero;

                for (int i = 0; i < count; i++) {
                    Vector3 p = forward * add * i;
                    p = Quaternion.AngleAxis(angle * i, normal) * p;
                    Line(position + lastP, position + p);
                    lastP = p;
                }
            }
        }
    }

    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            /// <summary>
            /// 绘制一个类似于 UI 滑块的“填充”矩形。值为0-1
            /// Draws a rect that gets "filled" similar to a UI slider. Value is in 0-1
            /// </summary>
            /// <param name="value">In 0-1</param>
            /// <param name="x">Rect x position</param>
            /// <param name="y">Rect y position</param>
            /// <param name="color"></param>
            /// <param name="width">Width of the rect</param>
            public static void Slider(float value, int x, int y, Color color, int width) {
                if (!UnityNDrawer.Exists) return;

                value = Mathf.Clamp01(value);

                Color c = new Color(1, 1, 1, 0.2f);

                SetFillColor(c);
                FillRect(x, y, width, 10);

                SetFillColor(color);
                FillRect(x, y, (int)(value * width), 10);
            }

            /// <summary>
            /// 绘制一个类似于 UI 滑块的“填充”矩形，其值为 -1到1，其中0位于中心。
            /// Draws a rect that gets "filled" similar to a UI slider with -1 to 1 values where 0 is in the center.
            /// </summary>
            /// <param name="value">In -1 to 1</param>
            /// <param name="x">Rect x position</param>
            /// <param name="y">Rect y position</param>
            /// <param name="color"></param>
            /// <param name="width">Width of the rect</param>
            public static void MidSlider(float value, int x, int y, Color color, int width) {
                if (!UnityNDrawer.Exists) return;

                value = Mathf.Clamp(value, -1, 1);

                Color c = new Color(1, 1, 1, 0.2f);

                SetFillColor(c);
                FillRect(x, y, width, 10);

                SetFillColor(color);
                FillRect(x + width * 0.5f, y, (int)(value * 0.5f * width), 10);
            }
        }
    }
    public static partial class NDrawHelper
    {
        public static partial class Screen
        {
            /// <summary>
            /// 绘制一个类似于 UI 网格矩形
            /// </summary>
            /// <param name="rect">Rect，定义在屏幕上以像素为单位绘制网格的位置Rect that defines where to draw the grid on screen, in pixels</param>
            /// <param name="limits">包含当前值的区域The area that encompasses the current value</param>
            /// <param name="unit">偏移与分离单元格The offset and separation of single cell</param>
            public static void SlidingGrid(Rect rect, Rect unit) {
                if (!UnityNDrawer.Exists) return;

                if (unit.height > 1) {
                    float off = unit.y % unit.height;
                    if (unit.y < 0) off += unit.height;

                    float start = rect.y + off;
                    float add = unit.height;

                    for (float y = start; y < rect.yMax; y += add) {
                        screenPoints.Add(new Vector3(rect.x, y));
                        screenPoints.Add(new Vector3(rect.xMax, y));
                    }
                }

                if (unit.width > 1) {
                    float off = unit.x % unit.width;
                    if (unit.x < 0) off += unit.width;

                    float start = rect.x + off;
                    float add = unit.width;

                    for (float x = start; x < rect.xMax; x += add) {
                        screenPoints.Add(new Vector3(x, rect.y));
                        screenPoints.Add(new Vector3(x, rect.yMax));
                    }
                }
            }
        }
    }

    public static partial class NDrawHelper
    {
        /// <summary>
        /// 扩展
        /// </summary>
        public static partial class World
        {
            /// <summary>
            /// 圆锥体
            /// </summary>
            /// <param name="position"></param>
            /// <param name="direction"></param>
            /// <param name="color"></param>
            /// <param name="angle"></param>
            public static void Cone(Vector3 position, Vector3 direction, float angle = 45) {
                float length = direction.magnitude;

                Vector3 _forward = direction;
                Vector3 _up = Vector3.Slerp(_forward, -_forward, 0.5f);
                Vector3 _right = Vector3.Cross(_forward, _up).normalized * length;

                direction = direction.normalized;

                Vector3 slerpedVector = Vector3.Slerp(_forward, _up, angle / 90.0f);

                float dist;
                var farPlane = new Plane(-direction, position + _forward);
                var distRay = new Ray(position, slerpedVector);

                farPlane.Raycast(distRay, out dist);

                Ray(position, slerpedVector.normalized * dist);
                Ray(position, Vector3.Slerp(_forward, -_up, angle / 90.0f).normalized * dist);
                Ray(position, Vector3.Slerp(_forward, _right, angle / 90.0f).normalized * dist);
                Ray(position, Vector3.Slerp(_forward, -_right, angle / 90.0f).normalized * dist);

                Circle(position + _forward, (_forward - (slerpedVector.normalized * dist)).magnitude, direction);
                Circle(position + (_forward * 0.5f), ((_forward * 0.5f) - (slerpedVector.normalized * (dist * 0.5f))).magnitude, direction);
            }
            /// <summary>
            /// 箭头
            /// </summary>
            /// <param name="position"></param>
            /// <param name="direction"></param>
            public static void Arrow(Vector3 position, Vector3 direction) {
                Ray(position, direction);
                Cone(position + direction, -direction * 0.333f, 15f);
            }
            /// <summary>
            /// 胶囊
            /// </summary>
            /// <param name="start"></param>
            /// <param name="end"></param>
            /// <param name="color"></param>
            /// <param name="radius"></param>
            /// <param name="duration"></param>
            /// <param name="depthTest"></param>
            public static void Capsule(Vector3 start, Vector3 end, float radius = 1) {
                Vector3 up = (end - start).normalized * radius;
                Vector3 forward = Vector3.Slerp(up, -up, 0.5f);
                Vector3 right = Vector3.Cross(up, forward).normalized * radius;

                float height = (start - end).magnitude;
                float sideLength = Mathf.Max(0, (height * 0.5f) - radius);
                Vector3 middle = (end + start) * 0.5f;

                start = middle + ((start - middle).normalized * sideLength);
                end = middle + ((end - middle).normalized * sideLength);

                //Radial circles
                Circle(start, radius, up);
                Circle(end, radius, -up);

                //Side lines
                Line(start + right, end + right);
                Line(start - right, end - right);

                Line(start + forward, end + forward);
                Line(start - forward, end - forward);

                for (int i = 1; i < 26; i++) {

                    //Start endcap
                    Line(Vector3.Slerp(right, -up, i / 25.0f) + start, Vector3.Slerp(right, -up, (i - 1) / 25.0f) + start);
                    Line(Vector3.Slerp(-right, -up, i / 25.0f) + start, Vector3.Slerp(-right, -up, (i - 1) / 25.0f) + start);
                    Line(Vector3.Slerp(forward, -up, i / 25.0f) + start, Vector3.Slerp(forward, -up, (i - 1) / 25.0f) + start);
                    Line(Vector3.Slerp(-forward, -up, i / 25.0f) + start, Vector3.Slerp(-forward, -up, (i - 1) / 25.0f) + start);

                    //End endcap
                    Line(Vector3.Slerp(right, up, i / 25.0f) + end, Vector3.Slerp(right, up, (i - 1) / 25.0f) + end);
                    Line(Vector3.Slerp(-right, up, i / 25.0f) + end, Vector3.Slerp(-right, up, (i - 1) / 25.0f) + end);
                    Line(Vector3.Slerp(forward, up, i / 25.0f) + end, Vector3.Slerp(forward, up, (i - 1) / 25.0f) + end);
                    Line(Vector3.Slerp(-forward, up, i / 25.0f) + end, Vector3.Slerp(-forward, up, (i - 1) / 25.0f) + end);
                }
            }
            /// <summary>
            /// 圆柱
            /// </summary>
            /// <param name="start"></param>
            /// <param name="end"></param>
            /// <param name="color"></param>
            /// <param name="radius"></param>
            /// <param name="duration"></param>
            /// <param name="depthTest"></param>
            public static void Cylinder(Vector3 start, Vector3 end, float radius = 1) {
                Vector3 up = (end - start).normalized * radius;
                Vector3 forward = Vector3.Slerp(up, -up, 0.5f);
                Vector3 right = Vector3.Cross(up, forward).normalized * radius;

                //Radial circles
                Circle(start, radius, up);
                Circle(end, radius, -up);
                Circle((start + end) * 0.5f, radius, up);

                //Side lines
                Line(start + right, end + right);
                Line(start - right, end - right);

                Line(start + forward, end + forward);
                Line(start - forward, end - forward);

                //Start endcap
                Line(start - right, start + right);
                Line(start - forward, start + forward);

                //End endcap
                Line(end - right, end + right);
                Line(end - forward, end + forward);
            }
            /// <summary>
            /// 球
            /// </summary>
            /// <param name="position"></param>
            /// <param name="color"></param>
            /// <param name="radius"></param>
            /// <param name="duration"></param>
            /// <param name="depthTest"></param>
            public static void Sphere(Vector3 position, float radius = 1.0f) {
                float angle = 10.0f;

                Vector3 x = new Vector3(position.x, position.y + radius * Mathf.Sin(0), position.z + radius * Mathf.Cos(0));
                Vector3 y = new Vector3(position.x + radius * Mathf.Cos(0), position.y, position.z + radius * Mathf.Sin(0));
                Vector3 z = new Vector3(position.x + radius * Mathf.Cos(0), position.y + radius * Mathf.Sin(0), position.z);

                Vector3 new_x;
                Vector3 new_y;
                Vector3 new_z;

                for (int i = 1; i < 37; i++) {

                    new_x = new Vector3(position.x, position.y + radius * Mathf.Sin(angle * i * Mathf.Deg2Rad), position.z + radius * Mathf.Cos(angle * i * Mathf.Deg2Rad));
                    new_y = new Vector3(position.x + radius * Mathf.Cos(angle * i * Mathf.Deg2Rad), position.y, position.z + radius * Mathf.Sin(angle * i * Mathf.Deg2Rad));
                    new_z = new Vector3(position.x + radius * Mathf.Cos(angle * i * Mathf.Deg2Rad), position.y + radius * Mathf.Sin(angle * i * Mathf.Deg2Rad), position.z);

                    Line(x, new_x);
                    Line(y, new_y);
                    Line(z, new_z);

                    x = new_x;
                    y = new_y;
                    z = new_z;
                }
            }

            public static void Point(Vector3 position, float scale = 1.0f) {

                Ray(position + (Vector3.up * (scale * 0.5f)), -Vector3.up * scale);
                Ray(position + (Vector3.right * (scale * 0.5f)), -Vector3.right * scale);
                Ray(position + (Vector3.forward * (scale * 0.5f)), -Vector3.forward * scale);
            }

        }
    }
}
