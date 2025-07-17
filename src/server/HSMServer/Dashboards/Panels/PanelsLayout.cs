using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;

namespace HSMServer.Dashboards
{
    internal static class PanelsLayout
    {
        private const double TotalWidthRow = 1.0D - PanelPadding; //remove right margin
        private const double DefaultYCoef = 0.22D; // coef for start point of Y coord for every row
        private const double PanelPadding = 0.01D;
        private const double SpaceBetween = 0.02D; // space between panels in Y
        private const int SingleModeMultiplayer = 2;
        private const int SingleModeMaxPanels = 5;

        internal static bool RecalculatePanelSize(ConcurrentDictionary<Guid, Panel> panelsDict, int panelsInRow)
        {
            try
            {
                var (panels, singleModePanels) = panelsDict.OrderBy(x => x.Value.Name?.ToLower()).SplitByCondition(x => x.Value.Settings.IsSingleMode);

                var defaultRowsCount = panels.Count / panelsInRow;
                var lastRowSize = panels.Count % panelsInRow;

                Relayout(panels.Take(defaultRowsCount * panelsInRow).ToList(), panelsInRow, out var y);
                Relayout(panels.TakeLast(lastRowSize).ToList(), lastRowSize, out y, defaultRowsCount); // + 1

                var rowsBefore = defaultRowsCount;

                if (lastRowSize != 0)
                    rowsBefore++;

                SingleModeRelayout(singleModePanels.ToList(), Math.Min(panelsInRow  * SingleModeMultiplayer, SingleModeMaxPanels), rowsBefore);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SingleModeRelayout(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, int rowsExist)
        {
            var panelWidth = TotalWidthRow / panelsInRow;

            var predY = new double[panelsInRow];
            for (var i = 0; i < panelsInRow; i++)
                predY[i] = DefaultYCoef * rowsExist;
           
            var j = 0;
            for (int i = 0; i < pairs.Count; ++i)
            {
                if (j >= panelsInRow)
                    j = 0;
                
                var (panelId, panel) = pairs[i];
                
                var columnNumber = i % panelsInRow;
                
                panel.Update(new PanelUpdate(panelId)
                {
                    Height = panel.Settings.SingleModeHeight,
                    Width = panelWidth - PanelPadding,

                    X = panelWidth * columnNumber + PanelPadding,
                    Y = predY[j],
                });
                
                predY[j] += panel.Settings.SingleModeHeight + SpaceBetween;

                j++;
            }
        }

        private static void Relayout(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, out double y, int rowsExist = 0)
        {
            if (panelsInRow <= 0)
                throw new ArgumentOutOfRangeException(nameof(panelsInRow), "The number of panels in a row should be greater 0.");

            ArgumentNullException.ThrowIfNull(pairs);

            var panelWidth = TotalWidthRow / panelsInRow;
            y = 0D;
            for (int i = 0; i < pairs.Count; ++i)
            {
                var (panelId, panel) = pairs[i];
                
                var rowNumber = i / panelsInRow + rowsExist;
                var columnNumber = i % panelsInRow;

                y = DefaultYCoef * rowNumber;
                
                panel.Update(new PanelUpdate(panelId)
                {
                    Height = PanelSettings.DefaultHeight,
                    Width = panelWidth - PanelPadding,

                    X = panelWidth * columnNumber + PanelPadding,
                    Y = y,
                });
            }
        }
    }
}