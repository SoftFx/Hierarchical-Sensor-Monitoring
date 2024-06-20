using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;

namespace HSMServer.Dashboards
{
    internal static class PanelsLayout
    {
        private const double TotalWidthRow = 1.0 - PanelPadding; //remove right margin
        private const double DefaultYCoef = 0.22; // coef for start point of Y coord for every row
        private const double PanelPadding = 0.01;


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
                
                var defaultRowsCount2 = singleModePanels.Count / (panelsInRow * 2);
                var lastRowSize2 = singleModePanels.Count % (panelsInRow * 2);
                
                Relayout(singleModePanels.Take(defaultRowsCount2 * panelsInRow * 2).ToList(), panelsInRow * 2, out y, rowsBefore); // +1 from pred
                
                if (defaultRowsCount2 * panelsInRow * 2 != 0)
                    rowsBefore++;
                
                Relayout(singleModePanels.TakeLast(lastRowSize2).ToList(), lastRowSize2, out y, rowsBefore + defaultRowsCount2);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SingleModeRelayout(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, int rowsExist = 0)
        {
            
        }

        private static void Relayout(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, out double y, int rowsExist = 0)
        {
            var panelWidth = TotalWidthRow / panelsInRow;
            y = 0D;
            var isSingleMode = false;
            for (int i = 0; i < pairs.Count; ++i)
            {
                var (panelId, panel) = pairs[i];
                
                isSingleMode = panel.Settings.IsSingleMode;
                
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

        private static void RelayoutSingleMode(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, out double y, int rowsExist = 0)
        {
            var panelWidth = TotalWidthRow / panelsInRow;
            y = 0D;
            var maxHeight = 0D;
            for (int i = 0; i < pairs.Count; ++i)
            {
                var (panelId, panel) = pairs[i];
             
                var rowNumber = i / panelsInRow + rowsExist;
                var columnNumber = i % panelsInRow;

                y = maxHeight == 0D? DefaultYCoef * rowNumber : DefaultYCoef * rowNumber ;
                
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