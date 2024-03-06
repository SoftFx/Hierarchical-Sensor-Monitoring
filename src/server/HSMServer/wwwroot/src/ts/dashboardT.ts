import {
    Plot,
    TimeSpanPlot
}
// @ts-ignore
    from "../js/plots.js";
import {currentPanel} from "../js/dashboard";
import {Data, PlotlyHTMLElement} from "plotly.js";

export namespace Dashboard {
    const updateDashboardInterval = 30000; // 2min
    const maxPlottedPoints = 1500;

    export interface Panel {
        id: string,
        panelId: string,
        sources: Source,
        requestTimeout: number
    }

    export interface Source {
        id: string,
        range: boolean | [number, number]
    }

    interface SourceUpdate {
        id: string,
        update: {
            newVisibleValues: Array<{
                value: number | string,
                id: number,
                time: string,
                tooltip: string
            }>,
            isTimeSpan: boolean
        }
    }

    export function initRequests(panel: Panel[]) {
        console.log(panel)
        let a = document.getElementById('panelChart_513a66d5-ee0c-42ad-afd3-ab77d549408a');
        
        for (let i in panel) {
            panel[i].requestTimeout = window.setInterval(function () {
                fetch(window.location.pathname + '/PanelUpdate' + `/${i}`, {
                    method: 'GET'
                }).then(res => res.json())
                    .then((res: SourceUpdate[]) => {
                        let plot = $(`#panelChart_${i}`)[0] as PlotlyHTMLElement;
                        for (let sourceUpdate of res) {
                            updateSource(sourceUpdate, plot)
                        }
                    })
            }, updateDashboardInterval)
        }
    }

    function updateSource(sourceUpdate: SourceUpdate, plot: PlotlyHTMLElement) {
        let visibleValues = sourceUpdate.update.newVisibleValues;
        let isTimeSpan = sourceUpdate.update.isTimeSpan !== undefined && sourceUpdate.update.isTimeSpan === true;
        let sourceId = sourceUpdate.id;

        let plotData = plot.data as Plot[];
        
        let correctId = 0;
        for (let j of plotData) {
            if (j.id === sourceId)
                break;
            
            correctId += 1;
        }

        let lastTime = new Date(0);

        if (plotData[correctId] !== undefined && plotData[correctId].x.length > 0)
            lastTime = new Date(plotData[correctId].x.at(-1));

        let prevData = plotData[correctId];
        let prevId = prevData.ids !== undefined && prevData.ids?.length !== 0 ? prevData.ids.at(-1) : undefined;
        if (prevData.ids === undefined)
            prevData.ids = [];
        let redraw = false;

        let x = [];
        let y = [];
        let customData = []
        for (let j of visibleValues) {
            if (lastTime >= new Date(j.time))
                continue;

            if (isTimeSpan) {
                let timespanValue = TimeSpanPlot.getTimeSpanValue(j);
                customData.push(TimeSpanPlot.getTimeSpanCustomData(timespanValue, j))
                x.push(j.time)
                y.push(timespanValue === 'NaN' ? timespanValue : timespanValue.totalMilliseconds())
            } else {
                if (prevId !== undefined && j.id === prevId) {
                    redraw = true;
                    prevData.x.pop();
                    prevData.y.pop();
                    prevData.customdata.pop();
                }
                x.push(j.time);
                y.push(j.value);
                prevData.ids.push(j.id)
                let custom = j.value;

                custom += `<br>${j.tooltip}`;
                
                // @ts-ignore
                if (currentPanel[sourceId].range !== undefined && currentPanel[sourceId].range !== true)
                    custom = j.tooltip;
                else if (j.tooltip !== null)
                    custom += `<br>${j.tooltip}`;

                customData.push(custom);
            }

        }

        if (x.length >= 1 && y.length >= 1 && (plot.data[correctId] as Plot).x[0] === null) {
            window.Plotly.update(plot, {x: [[]], y: [[]]}, {'xaxis.autorange': true}, correctId)
        }

        if (redraw) {
            prevData.x.push(...x)
            prevData.y.push(...y)
            prevData.customdata.push(...customData)
            window.Plotly.deleteTraces(plot, correctId);
            window.Plotly.addTraces(plot, prevData as Partial<Data>, correctId);
            DefaultRelayout(plot);
        } else {
            window.Plotly.extendTraces(plot, {
                y: [y],
                x: [x],
                customdata: [customData]
            }, [correctId], maxPlottedPoints).then(
                (data) => {
                    if (isTimeSpan)
                        TimespanRelayout(data);
                    else
                        DefaultRelayout(data);
                }
            )

        }
    }

    function TimespanRelayout(data: any) {
        let y = [];
        for (let i of data.data)
            y.push(...i.y)

        y = y.filter(element => {
            return element !== null;
        })

        let layoutTicks = TimeSpanPlot.getLayoutTicks(y);

        let layoutUpdate = {
            'yaxis.ticktext': layoutTicks[1] as string[],
            'yaxis.tickvals': layoutTicks[0] as string[]
        }
        
        // @ts-ignore
        window.Plotly.relayout(data.id, layoutUpdate)
    }

    function DefaultRelayout(data: any) {
        let layoutUpdate = {
            'xaxis.range': (window as any).getRangeDate(),
            'yaxis.autorange': true,
        }

        window.Plotly.relayout(data.id, layoutUpdate)
    }
}