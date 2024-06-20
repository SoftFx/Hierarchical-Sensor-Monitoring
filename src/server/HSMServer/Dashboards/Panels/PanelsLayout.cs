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

                Relayout(panels.Take(defaultRowsCount * panelsInRow).ToList(), panelsInRow);
                Relayout(panels.TakeLast(lastRowSize).ToList(), lastRowSize, defaultRowsCount); // + 1
                
                var rowsBefore = defaultRowsCount;

                if (lastRowSize != 0)
                    rowsBefore++;
                
                var defaultRowsCount2 = singleModePanels.Count / (panelsInRow * 2);
                var lastRowSize2 = singleModePanels.Count % (panelsInRow * 2);
                
                Relayout(singleModePanels.Take(defaultRowsCount2 * panelsInRow * 2).ToList(), panelsInRow * 2, rowsBefore); // +1 from pred
                
                if (defaultRowsCount2 * panelsInRow * 2 != 0)
                    rowsBefore++;
                
                Relayout(singleModePanels.TakeLast(lastRowSize2).ToList(), lastRowSize2, rowsBefore + defaultRowsCount2);

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

        private static void Relayout(List<KeyValuePair<Guid, Panel>> pairs, int panelsInRow, int rowsExist = 0)
        {
            var panelWidth = TotalWidthRow / panelsInRow;

            for (int i = 0; i < pairs.Count; ++i)
            {
                var (panelId, panel) = pairs[i];

                var rowNumber = i / panelsInRow + rowsExist;
                var columnNumber = i % panelsInRow;

                panel.Update(new PanelUpdate(panelId)
                {
                    Height = PanelSettings.DefaultHeight,
                    Width = panelWidth - PanelPadding,

                    X = panelWidth * columnNumber + PanelPadding,
                    Y = DefaultYCoef * rowNumber,
                });
            }
        }
    }
}