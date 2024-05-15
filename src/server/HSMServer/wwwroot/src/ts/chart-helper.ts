import Plotly from "plotly.js";
import {PanelSettings} from "./dashboard.classes";

export namespace ChartHelper {
    export async function initMultyichartCordinates(settings: PanelSettings, id: string) : Promise<number> {
        return new Promise(function (resolve, reject) {
            let dashboardPanels = $('#dashboardPanels');
            let width = dashboardPanels.width();
            let height = 1400;

            let currWidth = Number((settings.width * width).toFixed(5))
            let currHeight = Number((settings.height * height).toFixed(5))
            let transitionX = settings.x * width;
            let transitionY = settings.y * height;
            let panel = $(`#${id}`);

            if (panel.length === 0)
                reject();

            panel.width(currWidth)
                .height(currHeight)
                .css('transform', 'translate(' + transitionX + 'px, ' + transitionY + 'px)')
                .attr('data-x', transitionX)
                .attr('data-y', transitionY);

            resolve(transitionY + currHeight * 2);
        })
    }
    
    export async function initMultiChart(chartId: string, settings: PanelSettings, height = 300, autorange = false){
        return Plotly.newPlot(chartId, [], {
                hovermode: 'closest',
                hoverdistance: 1,
                dragmode: 'zoom',
                autosize: true,
                height: height,
                margin: {
                    // @ts-ignore
                    autoexpand: true,
                    l: 30,
                    r: 30,
                    t: 30,
                    b: 40,
                },
                showlegend: settings.showLegend,
                legend: {
                    y: 0,
                    x: 0,
                    orientation: "h",
                    yanchor: "bottom",
                    // @ts-ignore
                    yref: "container",
                },
                xaxis: {
                    type: 'date',
                    autorange: autorange,
                    automargin: true,
                    range: getRangeDate(),
                    title: {
                        //text: 'Time',
                        font: {
                            family: 'Courier New, monospace',
                            size: 18,
                            color: '#7f7f7f'
                        }
                    },
                    visible: false,
                    rangeslider: {
                        visible: false
                    }
                },
                yaxis: {
                    visible: false,
                    // @ts-ignore
                    automargin: 'width+right'
                }
            },
            {
                responsive: true,
                displaylogo: false,
                modeBarButtonsToRemove: [
                    'pan',
                    'lasso2d',
                    'pan2d',
                    'select2d',
                    'autoScale2d',
                    'autoScale2d',
                    'resetScale2d'
                ],
                modeBarButtonsToAdd: [
                    {
                        name: 'resetaxes',
                        _cat: 'resetscale',
                        title: 'Reset axes',
                        attr: 'zoom',
                        val: 'reset',
                        icon: Plotly.Icons.home,
                        click: (plot) => $(plot).trigger('plotly_doubleclick')
                    }],
                doubleClick: autorange ? 'reset+autosize' : autorange
            });
    }
    
    export function getRangeDate(){
        let period = $('#from_select').val();
        
        let currentDate = new Date(new Date(Date.now()).toUTCString());
        let lastDate = currentDate.toISOString()
        let newDate
        switch (period) {
            case "00:30:00":
                newDate = currentDate.setMinutes(currentDate.getMinutes() - 30)
                break
            case "01:00:00":
                newDate = currentDate.setHours(currentDate.getHours() - 1)
                break
            case "03:00:00":
                newDate = currentDate.setHours(currentDate.getHours() - 3)
                break
            case "06:00:00":
                newDate = currentDate.setHours(currentDate.getHours() - 6)
                break
            case "12:00:00":
                newDate = currentDate.setHours(currentDate.getHours() - 12)
                break
            case "1.00:00:00":
                newDate = currentDate.setDate(currentDate.getDate() - 1)
                break
            case "3.00:00:00":
                newDate = currentDate.setDate(currentDate.getDate() - 3)
                break
            case "7.00:00:00":
                newDate = currentDate.setDate(currentDate.getDate() - 7)
                break
            case "30.00:00:00":
                newDate = currentDate.setDate(currentDate.getDate() - 30)
                break

            default:
                newDate = currentDate.setHours(currentDate.getHours() - 6)
        }

        return [new Date(newDate).toISOString(), lastDate]
    }
}