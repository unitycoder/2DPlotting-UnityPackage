﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace IVLab.Plotting
{
    /// <summary>
    /// Parallel coordinates plot <see cref="DataPlot"/> implementation that uses Unity particle systems
    /// along with line renderers to efficiently render many data points at once.
    /// <img src="../resources/ParallelCoordsPlot/Example.png"/>
    /// </summary>
    public class ParallelCoordsPlot : DataPlot
    {
        // Editor-visible private variables
        [Header("Parallel Coords Plot Properties")]
        /// <summary> Size of the data points. </summary>
        [SerializeField] private float pointSize;
        /// <summary> Width of the line that connects the data points. </summary>
        [SerializeField] private float lineWidth;
        /// <summary> Controls whether or not the plot is scaled so that the 0 is visible in each column/axis. </summary>
        [SerializeField] private bool scaleToZero;
        /// <summary> The default color of lines in the plot. </summary>
        [SerializeField] protected Color32 defaultLineColor;
        /// <summary> The color of highlighted lines in the plot. </summary>
        [SerializeField] protected Color32 highlightedLineColor;
        /// <summary> The color of masked lines in the plot. </summary>
        [SerializeField] protected Color32 maskedLineColor;

        [Header("Parallel Coords Dependencies")]
        /// <summary> Prefab from which plot particles can be instantiated. </summary>
        [SerializeField] private GameObject plotParticleSystemPrefab;
        /// <summary> Prefab from which axis labels can be instantiated. </summary>
        [SerializeField] private GameObject axisLabelPrefab;
        /// <summary> Prefab from which line renderers can be instantiated. </summary>
        [SerializeField] private GameObject lineRendererPrefab;
        /// <summary> Prefab from which axis name label button can be instantiated. </summary>
        [SerializeField] private GameObject axisNameButtonPrefab;
        /// <summary> Parent used to store particle systems in the scene hierarchy. </summary>
        [SerializeField] private Transform plotParticlesParent;
        /// <summary> Parent used to store line renderers in the scene hierarchy. </summary>
        [SerializeField] private Transform lineRendererParent;
        /// <summary> Parent used to store axes labels in the scene hierarchy. </summary>
        [SerializeField] private Transform axisLabelsParent;

        // Editor-non-visible private variables
        /// <summary> Matrix (column-major) of point positions in each column/axis of the plot. </summary>
        private Vector2[][] pointPositions;
        /// <summary> Matrix (column-major) of whether or not each point is NaN. Allows for NaN values to be loaded into the
        /// data table, but then not be rendered. </summary>
        protected bool[][] pointIsNaN;
        /// <summary> Array of particle systems used to render data points in each column/axis. </summary>
        private ParticleSystem[] plotParticleSystem;
        /// <summary> Matrix (column-major) of particles representing all the points on the plot. </summary>
        private ParticleSystem.Particle[][] pointParticles;
        /// <summary> Matrix of (column-major) line renderers for the connections between every point in each data point. </summary>
        private LineRenderer[][] lineRenderers;
        /// <summary> Array of axis label scripts for each column/axis of the plot. </summary>
        private NiceAxisLabel[] axisLabels;
        /// <summary> Array of axis name buttons that display the names of each axis and can be clicked to flip them. </summary>
        private Button[] axisNameButtons;
        /// <summary> Indices into pointPositions matrix of the point currently selected by the click selection mode. </summary>
        private (int, int) clickedPointIdx;

#if UNITY_EDITOR
        private float screenHeight;
#endif  // UNITY_EDITOR

        // Self-initialization.
        void Awake()
        {
#if UNITY_EDITOR
            screenHeight = Screen.height;
#endif  // UNITY_EDITOR
        }

        /// <summary>
        /// Initializes the parallel coords plot by initializing its particle systems, line renderers, axis labeling scripts,
        /// and axis-flipping buttons.
        /// </summary>
        /// <param name="dataPlotManager"> Manager of the plot: contains references to the <see cref="DataTable"/> and 
        /// <see cref="LinkedIndices"/> that the plot works from. </param>
        /// <param name="outerBounds"> Size to set the outer bounds of the plot. </param>
        /// <param name="selectedDataPointIndices"> Array of data point indices the plot should display.
        /// If <c>null</c>, all data points will be displayed by default. </param>
        public override void Init(DataPlotManager dataPlotManager, Vector2 outerBounds, int[] selectedDataPointIndices = null)
        {
            // Perform generic data plot initialization
            base.Init(dataPlotManager, outerBounds, selectedDataPointIndices);

            // Initialize point position and particle matrices/arrays
            pointPositions = new Vector2[dataTable.Width][];
            pointParticles = new ParticleSystem.Particle[dataTable.Width][];
            pointIsNaN = new bool[dataTable.Width][];
            // Create an instance of the point particle system for each column/axis
            plotParticleSystem = new ParticleSystem[dataTable.Width];
            for (int j = 0; j < dataTable.Width; j++)
            {
                pointPositions[j] = new Vector2[this.selectedDataPointIndices.Length];
                pointParticles[j] = new ParticleSystem.Particle[this.selectedDataPointIndices.Length];
                pointIsNaN[j] = new bool[this.selectedDataPointIndices.Length];
                // Instantiate a point particle system GameObject
                GameObject plotParticleSystemInst = Instantiate(plotParticleSystemPrefab, Vector3.zero, Quaternion.identity) as GameObject;
                // Reset its size and position
                plotParticleSystemInst.transform.SetParent(plotParticlesParent);
                plotParticleSystemInst.transform.localScale = Vector3.one;
                plotParticleSystemInst.transform.localPosition = Vector3.zero;
                // Add its particle system component to the array of particle systems
                plotParticleSystem[j] = plotParticleSystemInst.GetComponent<ParticleSystem>();
                plotParticleSystem[j].Pause();
            }

            // Create an instance of the plot line renderer system for every data points for every 3 columns/axes
            lineRenderers = new LineRenderer[Mathf.FloorToInt(dataTable.Width/2.0f)][];
            for (int j = 0; j < lineRenderers.Length; j++)
            {
                lineRenderers[j] = new LineRenderer[this.selectedDataPointIndices.Length];
                for (int i = 0; i < this.selectedDataPointIndices.Length; i++)
                {
                    // Instantiate a line render GameObject
                    GameObject lineRendererGO = Instantiate(lineRendererPrefab, Vector3.zero, Quaternion.identity) as GameObject;
                    // Reset its size and position
                    lineRendererGO.transform.SetParent(lineRendererParent);
                    lineRendererGO.transform.localScale = Vector3.one;
                    lineRendererGO.transform.localPosition = Vector3.zero;
                    // Add its line renderer component to the array of line renderers
                    lineRenderers[j][i] = lineRendererGO.GetComponent<LineRenderer>();
                    // If the table has an even width, its final line renderer will only have 2 points,
                    // otherwise each line renderer should have 3 points
                    lineRenderers[j][i].positionCount = dataTable.Width % 2 == 0 ? 2 : 3;
                }
            }

            // Create an instance of an axis label and a axis name for each column/axis
            axisLabels = new NiceAxisLabel[dataTable.Width];
            axisNameButtons = new Button[dataTable.Width];
            for (int j = 0; j < axisLabels.Length; j++)
            {
                // Instantiate a axis label GameObject
                GameObject axisLabel = Instantiate(axisLabelPrefab, Vector3.zero, Quaternion.identity) as GameObject;
                // Reset its size and position
                axisLabel.transform.SetParent(axisLabelsParent);
                axisLabel.transform.localScale = Vector3.one;
                axisLabel.transform.localPosition = Vector3.zero;
                // Add its nice axis label script component to the array of axis label scripts
                axisLabels[j] = axisLabel.GetComponent<NiceAxisLabel>();

                // Instantiate a axis name GameObject
                GameObject axisNameButtonInst = Instantiate(axisNameButtonPrefab, Vector3.zero, Quaternion.identity) as GameObject;
                // Reset its size and position
                axisNameButtonInst.transform.SetParent(axisLabel.transform);
                axisNameButtonInst.transform.localScale = Vector3.one;
                axisNameButtonInst.transform.localPosition = Vector3.zero;
                // Add its button to the array of axis name buttons
                axisNameButtons[j] = axisNameButtonInst.GetComponent<Button>();
                // Add a callback to then button to flip its related axis
                int columnIdx = j;
                axisNameButtons[j].onClick.AddListener(delegate { FlipAxis(columnIdx); });

                // Add pointer enter and exit triggers to disable and enable selection when
                // buttons are being pressed
                EventTrigger eventTrigger = axisNameButtonInst.GetComponent<EventTrigger>();
                EventTrigger.Entry pointerEnter = new EventTrigger.Entry();
                pointerEnter.eventID = EventTriggerType.PointerEnter;
                pointerEnter.callback.AddListener(delegate { dataPlotManager.DisableSelection(); });
                eventTrigger.triggers.Add(pointerEnter);
                EventTrigger.Entry pointerExit = new EventTrigger.Entry();
                pointerExit.eventID = EventTriggerType.PointerExit;
                pointerExit.callback.AddListener(delegate { dataPlotManager.EnableSelection(); });
                eventTrigger.triggers.Add(pointerExit);
            }

            // Modify all data points according to current state of index space
            foreach (int i in this.selectedDataPointIndices)
            {
                UpdateDataPoint(i, linkedIndices[i]);
            }
        }

        // Manages mouse input with current selection mode.
        void Update()
        {
            // Ensures the plot is always drawn and scaled correctly in editor mode even if the screen height changes
#if UNITY_EDITOR
            if (Screen.height != screenHeight)
            {
                Plot();
                screenHeight = Screen.height;
            }
#endif  // UNITY_EDITOR

        }

        /// <summary>
        /// Updates a specified data point (which for a parallel coords plot includes multiple 
        /// point particles and their line renderer) based on its linked index attributes, only if it is
        /// already within the selected subset of points that this graph plots.
        /// </summary>
        /// <param name="index">Index of data point that needs to be updated.</param>
        /// <param name="indexAttributes">Current attributes of the data point.</param>
        public override void UpdateDataPoint(int index, LinkedIndices.LinkedAttributes indexAttributes)
        {
            if (selectedIndexDictionary.ContainsKey(index))
            {
                int i = selectedIndexDictionary[index];
                if (indexAttributes.Masked)
                {
                    for (int j = 0; j < dataTable.Width; j++)
                    {
                        // Mask the points
                        pointParticles[j][i].startColor = maskedColor;
                        // Mask the lines
                        if (j < lineRenderers.Length)
                        {
                            lineRenderers[j][i].startColor = maskedLineColor;
                            lineRenderers[j][i].endColor = maskedLineColor;
                        }
                    }
                }
                else if (indexAttributes.Highlighted)
                {

                    for (int j = 0; j < dataTable.Width; j++)
                    {
                        // Mask the points
                        pointParticles[j][i].startColor = highlightedColor;
                        // Hack to ensure highlighted particle appears in front of non-highlighted particles
                        pointParticles[j][i].position = new Vector3(pointParticles[j][i].position.x, pointParticles[j][i].position.y, -0.01f);
                        // Mask the lines
                        if (j < lineRenderers.Length)
                        {
                            lineRenderers[j][i].startColor = highlightedLineColor;
                            lineRenderers[j][i].endColor = highlightedLineColor;
                            lineRenderers[j][i].sortingOrder = 3;
                        }
                    }
                }
                else
                {
                    for (int j = 0; j < dataTable.Width; j++)
                    {
                        // Mask the points
                        pointParticles[j][i].startColor = defaultColor;
                        // Hack to ensure highlighted particle appears in front of non-highlighted particles
                        pointParticles[j][i].position = new Vector3(pointParticles[j][i].position.x, pointParticles[j][i].position.y, 0);
                        // Mask the lines
                        if (j < lineRenderers.Length)
                        {
                            lineRenderers[j][i].startColor = defaultLineColor;
                            lineRenderers[j][i].endColor = defaultLineColor;
                            lineRenderers[j][i].sortingOrder = 1;
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Updates the point particle systems to reflect the current state of the 
        /// data point particles.
        /// </summary>
        /// <remarks>
        /// Usually called after a series of UpdateDataPoint() calls to ensure
        /// that those updates are visually reflected.
        /// </remarks>
        public override void RefreshPlotGraphics()
        {
            for (int j = 0; j < plotParticleSystem.Length; j++)
            {
                plotParticleSystem[j].SetParticles(pointParticles[j], pointParticles[j].Length);
            }
        }

        /// <summary>
        /// Flips the j'th axis of the plot.
        /// </summary>
        /// <param name="j">Index into the data table for the column/axis that should be flipped. </param>
        public void FlipAxis(int j)
        {
            // Toggle the inverted status of the axis
            axisLabels[j].Inverted = !axisLabels[j].Inverted;

            // Determine the axis source position based on inversion and offset
            Vector2 axisSource;
            Vector2 axisOffset = Vector2.right * innerBounds.x / (dataTable.Width - 1) * j;
            if (axisLabels[j].Inverted)
            {
                axisSource = plotOuterRect.anchoredPosition + axisOffset + new Vector2(-innerBounds.x, innerBounds.y) / 2;
            }
            else
            {
                axisSource = plotOuterRect.anchoredPosition + axisOffset + new Vector2(-innerBounds.x, -innerBounds.y) / 2;
            }

            // Regenerate the axis labels
            axisLabels[j].GenerateYAxisLabel(axisSource, innerBounds);

            // Reposition just the point particles and line renderer points in this column/axis to match the flip
            float columnMin = axisLabels[j].NiceMin;
            float columnMax = axisLabels[j].NiceMax;
            float columnScale = innerBounds.y / (columnMax - columnMin);
            for (int i = 0; i < selectedDataPointIndices.Length; i++)
            {
                // Get the index of the actual data point
                int dataPointIndex = selectedDataPointIndices[i];
                // Only try to flip the point if it isn't NaN
                float dataValue = dataTable.Data(dataPointIndex, j);
                if (!float.IsNaN(dataValue))
                {
                    float x = axisSource.x;
                    // Position points along the y axis depending on the inversion
                    float y;
                    if (axisLabels[j].Inverted)
                    {
                        y = axisSource.y - (dataTable.Data(dataPointIndex, j) - columnMin) * columnScale;
                    }
                    else
                    {
                        y = axisSource.y + (dataTable.Data(dataPointIndex, j) - columnMin) * columnScale;
                    }
                    pointPositions[j][i] = new Vector2(x, y);
                    pointParticles[j][i].position = new Vector3(x, y, 0) * plotsCanvas.transform.localScale.y + Vector3.forward * pointParticles[j][i].position.z;  // scale by canvas size since particles aren't officially part of the canvas
                    // Flipping the line renderers is pretty obnoxious with the current segmented setup,
                    // but in short we ask the line renderer helper to give us a list of tuples, where the first
                    // item in each tuple is the index of the linerenderer that needs to be flipped, and the second
                    // item is the point inside that line renderer that needs to be flipped
                    (int, int)[] flippedLineRenderInfo = LineRendererHelper.FlipAxis(j);
                    for (int k = 0; k < flippedLineRenderInfo.Length; k++)
                    {
                        lineRenderers[flippedLineRenderInfo[k].Item1][i].SetPosition(flippedLineRenderInfo[k].Item2, new Vector3(x, y, 0));
                    }
                }
            }

            // Update the particles to match
            plotParticleSystem[j].SetParticles(pointParticles[j], pointParticles[j].Length);
        }

        /// <summary>
        /// Plots only the selected data in the data table, updating all particle systems and line renderers.
        /// </summary>
        public override void Plot()
        {
            // Determine the spacing between columns/axis
            float spacing = innerBounds.x / (dataTable.Width - 1);

            // Iterate through each column/axis and plot it
            for (int j = 0; j < dataTable.Width; j++)
            {
                // Extract the min and max values for this column/axis from the data table
                float columnMin = selectedDataPointMins[j];
                float columnMax = selectedDataPointMaxes[j];
                if (scaleToZero)
                {
                    columnMin = (columnMin > 0) ? 0 : columnMin;
                    columnMax = (columnMax < 0) ? 0 : columnMax;
                }
                // Instantiate a new axis label, first by generating "nice" min and max values and then by generating
                // the actual axis
                // Determine the axis source position based on inversion and offset
                Vector2 axisSource;
                Vector2 axisOffset = Vector2.right * innerBounds.x / (dataTable.Width - 1) * j;
                if (axisLabels[j].Inverted)
                {
                    axisSource = plotOuterRect.anchoredPosition + axisOffset + new Vector2(-innerBounds.x, innerBounds.y) / 2;
                }
                else
                {
                    axisSource = plotOuterRect.anchoredPosition + axisOffset + new Vector2(-innerBounds.x, -innerBounds.y) / 2;
                }
                (columnMin, columnMax) = axisLabels[j].GenerateNiceMinMax(columnMin, columnMax);
                axisLabels[j].GenerateYAxisLabel(axisSource, innerBounds);

                // Set the position and text of column/axis name
                if (axisLabels[j].Inverted)
                {
                    axisNameButtons[j].GetComponent<RectTransform>().anchoredPosition3D = axisSource + Vector2.down * (innerBounds.y + padding.y / 4);
                }
                else
                {
                    axisNameButtons[j].GetComponent<RectTransform>().anchoredPosition3D = axisSource + Vector2.down * padding.y / 4;
                }
                axisNameButtons[j].GetComponentInChildren<TextMeshProUGUI>().text = dataTable.ColumnNames[j];

                // Determine a rescaling of this column/axis's data based on adjusted ("nice"-fied) min and max
                float columnScale = innerBounds.y / (columnMax - columnMin);

                // Iterate through all data points in this column/axis and position/scale particle and linerenderer points
                for (int i = 0; i < selectedDataPointIndices.Length; i++)
                {
                    // Get the index of the actual data point
                    int dataPointIndex = selectedDataPointIndices[i];
                    // If the point is NaN, flag it so that it will be unselectable and set its size to 0 so it will be invisible
                    float dataValue = dataTable.Data(dataPointIndex, j);
                    if (float.IsNaN(dataValue))
                    {
                        pointIsNaN[j][i] = true;
                        // Hide the point by setting its size to 0
                        pointParticles[j][i].startSize = 0;
                    }
                    // Otherwise position and size the point normally
                    else
                    {
                        pointIsNaN[j][i] = false;
                        // Determine the x and y position of the current data point based on the adjusted rescaling
                        float x = axisSource.x;
                        float y;
                        if (axisLabels[j].Inverted)
                        {
                            y = axisSource.y - (dataTable.Data(dataPointIndex, j) - columnMin) * columnScale;
                        }
                        else
                        {
                            y = axisSource.y + (dataTable.Data(dataPointIndex, j) - columnMin) * columnScale;
                        }
                        // Position and scale the point particles and line renderers
                        pointPositions[j][i] = new Vector2(x, y);
                        pointParticles[j][i].position = new Vector3(x, y, 0) * plotsCanvas.transform.localScale.y + Vector3.forward * pointParticles[j][i].position.z;  // scale by canvas size since particles aren't officially part of the canvas
                        pointParticles[j][i].startSize = pointSize * plotsCanvas.transform.localScale.y * Mathf.Max(outerBounds.x, outerBounds.y) / 300;
                    }
                    // Only update the line renderers when on an even column/axis or at the final column/axis (since each line renderer is responsible for connecting three columns/axes)
                    bool firstColumn = j == 0;
                    bool evenColumn = j % 2 == 0;
                    bool finalColumn = j == dataTable.Width - 1;
                    if (!firstColumn && (evenColumn || finalColumn))
                    {
                        // Determine the idx of the current line renderer
                        int lineRendererIdx = Mathf.CeilToInt(j / 2.0f) - 1;
                        // Determine the max number of points in the current line renderer
                        // (should usually be three, unless this is an odd final column/axis)
                        int maxPointsCount = (evenColumn) ? 3 : 2;
                        // Determine which points that this line renderer is in charge of aren't NaN
                        bool[] pointSetup = new bool[maxPointsCount];
                        for (int k = j - maxPointsCount + 1, l = 0; k <= j; k++, l++)
                        {
                            pointSetup[l] = (!pointIsNaN[k][i]) ? true : false;
                        }
                        // Use the LineHelper to determine if a line can be drawn to connect the valid points
                        int[] validIndexOffsets;  // This will contain offsets from j for the valid points that should be drawn, if there are any
                        if (LineRendererHelper.ValidSetup(pointSetup, out validIndexOffsets))
                        {
                            lineRenderers[lineRendererIdx][i].positionCount = validIndexOffsets.Length;
                            for (int idx = 0; idx < validIndexOffsets.Length; idx++)
                            {
                                lineRenderers[lineRendererIdx][i].SetPosition(idx, pointPositions[j+ validIndexOffsets[idx]][i]);
                            }
                        }
                        // If a line can't connect them, set the number of points this line renders to 0
                        else
                        {
                            lineRenderers[lineRendererIdx][i].positionCount = 0;
                        }
                    }
                    // Set the width of the line renderer for this data point
                    if (j < lineRenderers.Length)
                    {
                        lineRenderers[j][i].startWidth = lineWidth * plotsCanvas.transform.localScale.y * Mathf.Max(outerBounds.x, outerBounds.y) / 300;
                        lineRenderers[j][i].endWidth = lineWidth * plotsCanvas.transform.localScale.y * Mathf.Max(outerBounds.x, outerBounds.y) / 300;
                    }
                }
            }
            // Refresh the plot graphics to match the plotting changes made
            RefreshPlotGraphics();
        }

        /// <summary>
        /// Selects the point within the point selection radius that is closest to the mouse selection position if the selection state
        /// is "Start", and otherwise simply checks to see if the initially selected point is still within the point selection radius,
        /// highlighting it if it is, unhighlighting it if it is not.
        /// </summary>
        /// <remarks>
        /// For a parallel coords plot, a "data point" consists of multiple point particles, any of which could be selected.
        /// </remarks>
        /// <param name="selectionPosition">Current selection position.</param>
        /// <param name="selectionState">State of the selection, e.g. Start/Update/End.</param>
        public override void ClickSelection(Vector2 selectionPosition, SelectionMode.State selectionState)
        {
            // Square the selection radius to avoid square root computation in the future
            float selectionRadiusSqr = Mathf.Pow(clickSelectionRadius, 2);
            // If this is the initial click, i.e. selectionState is Start, find the closest particle to the mouse (within selection radius) 
            // and highlight it, unhighlighting all other points
            if (selectionState == SelectionMode.State.Start)
            {
                // Reset clicked point index to -1 to reflect that no data points have been clicked
                clickedPointIdx = (-1, -1);
                // Set the current minimum distance (squared) between mouse and any point to the selection radius (squared)
                float minDistSqr = selectionRadiusSqr;
                // Iterate through all points to see if any are closer to the mouse than the current min distance,
                // updating the min distance every time a closer point is found
                for (int i = 0; i < linkedIndices.Size; i++)
                {
                    if (selectedIndexDictionary.ContainsKey(i))
                    {
                        for (int j = 0; j < pointPositions.Length; j++)
                        {
                            // NaN points are unselectable
                            if (!pointIsNaN[j][selectedIndexDictionary[i]]) {
                                float mouseToPointDistSqr = Vector2.SqrMagnitude(selectionPosition - pointPositions[j][selectedIndexDictionary[i]]);
                                // Only highlight the point if it is truly the closest one to the mouse
                                if (mouseToPointDistSqr < selectionRadiusSqr && mouseToPointDistSqr < minDistSqr)
                                {
                                    // Unhighlight the previous closest point to the mouse since it is no longer the closest
                                    // (as long as it was not already relating to the same data point idx)
                                    if (clickedPointIdx != (-1, -1) && clickedPointIdx.Item1 != i)
                                    {
                                        linkedIndices[clickedPointIdx.Item1].Highlighted = false;
                                    }
                                    // Highlight the new closest point
                                    minDistSqr = mouseToPointDistSqr;
                                    clickedPointIdx = (i, j);
                                    // Only highlight the data point if it isn't masked
                                    if (!linkedIndices[i].Masked)
                                    {
                                        linkedIndices[i].Highlighted = true;
                                    }
                                }
                            }
                        }
                        // Since all the individual points in a "row" are related to a single "data point",
                        // if not a single point in this row was clicked on, make sure not to highlight
                        // this entire data point
                        if (clickedPointIdx.Item1 != i)
                        {
                            linkedIndices[i].Highlighted = false;
                        }
                    }
                    else
                    {
                        linkedIndices[i].Highlighted = false;
                    }
                }
            }
            // If this is not the initial click but their was previously a point that was selected/clicked,
            // check to see if that point is still within the point selection radius of the current mouse selection position
            else if (clickedPointIdx != (-1, -1))
            {
                int i = selectedIndexDictionary[clickedPointIdx.Item1];
                int j = clickedPointIdx.Item2;
                // NaN points are unselectable
                if (!pointIsNaN[j][i])
                {
                    float mouseToPointDistSqr = Vector2.SqrMagnitude(selectionPosition - pointPositions[j][i]);
                    if (mouseToPointDistSqr < selectionRadiusSqr)
                    {
                        // Only highlight the data point if it isn't masked
                        if (!linkedIndices[clickedPointIdx.Item1].Masked)
                        {
                            linkedIndices[clickedPointIdx.Item1].Highlighted = true;
                        }
                    }
                    else
                    {
                        linkedIndices[clickedPointIdx.Item1].Highlighted = false;
                    }
                }
            }
        }

        /// <summary>
        /// Selects all of the data points inside the given selection rectangle.
        /// </summary>
        /// <remarks>
        /// For a parallel coords plot, a "data point" consists of multiple point particles, any of which could be selected.
        /// </remarks>
        /// <param name="selectionRect">Transform of the selection rectangle.</param>
        public override void RectSelection(RectTransform selectionRect)
        {
            for (int i = 0; i < linkedIndices.Size; i++)
            {
                if (selectedIndexDictionary.ContainsKey(i))
                {
                    bool rectContainsPoint = false;
                    // Check if any of the points that make up this "data point" (where for a parallel coords 
                    // plot a "data point" is a line renderer and series of points it connects) are inside
                    // the selection rect. If any of the individual points are inside the selection rect,
                    // highlight the entire "data point" that point is related to.
                    for (int j = 0; j < pointPositions.Length; j++)
                    {
                        // NaN points are unselectable
                        if (!pointIsNaN[j][selectedIndexDictionary[i]])
                        {
                            // Must translate point position to anchored position space space for rect.Contains() to work
                            rectContainsPoint = selectionRect.rect.Contains(pointPositions[j][selectedIndexDictionary[i]] - selectionRect.anchoredPosition);
                            if (rectContainsPoint) break;
                        }
                    }
                    if (rectContainsPoint)
                    {
                        // Only highlight the data point if it isn't masked
                        if (!linkedIndices[i].Masked)
                        {
                            linkedIndices[i].Highlighted = true;
                        }
                    }
                    else
                    {
                        linkedIndices[i].Highlighted = false;
                    }
                }
                else
                {
                    linkedIndices[i].Highlighted = false;
                }
            }
        }

        /// <summary>
        /// Selects all the data points that the brush has passed over.
        /// </summary>
        /// <remarks>
        /// For a parallel coords plot, a "data point" consists of multiple point particles, any of which could be selected.
        /// </remarks>
        /// <param name="prevBrushPosition">Previous position of the brush.</param>
        /// <param name="brushDelta">Change in position from previous to current.</param>
        /// <param name="selectionState">State of the selection, e.g. Start/Update/End.</param>
        public override void BrushSelection(Vector2 prevBrushPosition, Vector2 brushDelta, SelectionMode.State selectionState)
        {
            // Square the brush radius to avoid square root computation in the future
            float brushRadiusSqr = Mathf.Pow(brushSelectionRadius, 2);
            // This only triggers when brush selection is first called, therefore we can use it as an indicator
            // that we should reset all points except for those currently within the radius of the brush
            if (selectionState == SelectionMode.State.Start)
            {
                for (int i = 0; i < linkedIndices.Size; i++)
                {
                    if (selectedIndexDictionary.ContainsKey(i))
                    {
                        for (int j = 0; j < pointPositions.Length; j++)
                        {
                            // NaN points are unselectable
                            if (!pointIsNaN[j][selectedIndexDictionary[i]])
                            {
                                // Highlight any points within the radius of the brush and unhighlight any that aren't
                                float pointToBrushDistSqr = Vector2.SqrMagnitude(pointPositions[j][selectedIndexDictionary[i]] - prevBrushPosition);
                                if (pointToBrushDistSqr < brushRadiusSqr)
                                {
                                    // Only highlight the data point if it isn't masked
                                    if (!linkedIndices[i].Masked)
                                    {
                                        linkedIndices[i].Highlighted = true;
                                        break;
                                    }
                                }
                                else
                                {
                                    linkedIndices[i].Highlighted = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        linkedIndices[i].Highlighted = false;
                    }
                }
            }
            // If this isn't the start of the selection, iterate through all data point positions
            // to highlight those which have been selected by the brush (taking into account the full movement 
            // of the brush since the previous frame)
            else
            {
                for (int i = 0; i < selectedDataPointIndices.Length; i++)
                {
                    // Get the index of the actual data point
                    int dataPointIndex = selectedDataPointIndices[i];
                    for (int j = 0; j < pointPositions.Length; j++)
                    {
                        if (!pointIsNaN[j][dataPointIndex])
                        {
                            // Trick to parametrize the line segment that the brush traveled since last frame and find the closest
                            // point on it to the current plot point
                            float t = Mathf.Max(0, Mathf.Min(1, Vector2.Dot(pointPositions[j][i] - prevBrushPosition, brushDelta) / brushDelta.sqrMagnitude));
                            Vector2 closestPointOnLine = prevBrushPosition + t * brushDelta;
                            // Determine if point lies within the radius of the closest point to it on the line
                            float pointToBrushDistSqr = Vector2.SqrMagnitude(pointPositions[j][i] - closestPointOnLine);
                            if (pointToBrushDistSqr < brushRadiusSqr)
                            {
                                // Only highlight the data point if it isn't masked
                                if (!linkedIndices[dataPointIndex].Masked)
                                {
                                    linkedIndices[dataPointIndex].Highlighted = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper class to make new NaN-proof line renderer setup
        /// more concise.
        /// </summary>
        private class LineRendererHelper
        {
            /// <summary>
            /// Determines whether the given point setup is valid, and if it is returns a set of offsets
            /// to reach the valid points that should be connected.
            /// </summary>
            /// <param name="pointSetup">Array of bools corresponding to whether each point is valid (not NaN) or not.</param>
            /// <param name="validPointIndices">Returned list of index offsets to the points that should be connected.</param>
            /// <returns></returns>
            public static bool ValidSetup(bool[] pointSetup, out int[] validIndexOffsets)
            {
                if (pointSetup.Length == 2 && pointSetup[0] && pointSetup[1])
                {
                    validIndexOffsets = new int[] { -1, 0 };
                    return true;
                }
                else if (pointSetup.Length == 3 && pointSetup[0] && pointSetup[1] && pointSetup[2])
                {
                    validIndexOffsets = new int[] { -2, -1, -0 };
                    return true;
                }
                else if (pointSetup.Length == 3 && pointSetup[0] && pointSetup[1])
                {
                    validIndexOffsets = new int[] { -2, -1};
                    return true;
                }
                else if (pointSetup.Length == 3 && pointSetup[1] && pointSetup[2])
                {
                    validIndexOffsets = new int[] { -1, -0 };
                    return true;
                }
                else
                {
                    validIndexOffsets = null;
                    return false;
                }
            }

            /// <summary>
            /// Helps to the line renderers connected to a specific axis by both determining the indices
            /// of the affected line renderers as well as the indices of the points into those line renderers
            /// that are affects.
            /// </summary>
            /// <param name="j">Index into the data table for the column/axis that is being flipped. </param>
            /// <returns></returns>
            public static (int, int)[] FlipAxis(int j)
            {
                // Determine the indices of the one to two line renderers that have points on this axis
                int lineRendererIndex1 = Mathf.CeilToInt(j / 2.0f) - 1;
                int lineRendererIndex2 = Mathf.FloorToInt(j / 2.0f);
                // Two line renderers
                if (lineRendererIndex1 != lineRendererIndex2)
                {
                    (int, int)[] res = new (int, int)[2];
                    res[0].Item1 = lineRendererIndex1;
                    res[1].Item1 = lineRendererIndex2;

                    res[0].Item2 = 2;
                    res[0].Item2 = 0;
                    return res;
                }
                // One line renderer 
                else
                {
                    (int, int)[] res = new (int, int)[1];
                    res[0].Item1 = lineRendererIndex1;
                    if (j == 0) {
                        res[0].Item2 = 0;
                    }
                    else
                    {
                        res[0].Item2 = 1;
                    }
                    return res;
                }
            }
        }
    }
}
