using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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
                var panels = panelsDict.OrderBy(x => x.Value.Name?.ToLower()).ToList();

                var defaultRowsCount = panels.Count / panelsInRow;
                var lastRowSize = panels.Count % panelsInRow;

                Relayout(panels.Take(defaultRowsCount * panelsInRow).ToList(), panelsInRow);
                Relayout(panels.TakeLast(lastRowSize).ToList(), lastRowSize, defaultRowsCount);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
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