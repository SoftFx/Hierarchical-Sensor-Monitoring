import {PlotlyHTMLElement} from "plotly.js";
import {Panel, PlotUpdate, SourceUpdate} from "./dashboard.interfaces";
import {Plot, TimeSpanPlot} from "../js/plots";


export namespace DataUpdate {
    export class Update {
        private panel: Panel;
        

        updateData: { update: PlotUpdate, id: number }[] = [];
        redrawData: { plot: Plot, id: number }[] = [];
        singleUpdate: PlotUpdate = new PlotUpdate();
        isTimeSpan: boolean = false;


        public constructor(panel: Panel) {
            this.panel = panel;
        }

        async updateSources(sourceUpdates: SourceUpdate[]) {
            let promises: Promise<boolean>[] = [];
            let plotDiv = $(`#panelChart_${this.panel.id}`)[0] as PlotlyHTMLElement;
            console.log(plotDiv)
            for (let sourceUpdate of sourceUpdates)
                promises.push(this.updateSource(sourceUpdate, plotDiv));

            await Promise.allSettled(promises).then((results) => {
                if (results.every((result) => {
                    console.log(result.status)
                    return result.status === "fulfilled";
                })) {
                    let [update, ids] = this.getUpdates();
                    this.extendTraces(plotDiv, update, ids);
                }
            })
        }

        private async updateSource(sourceUpdate: SourceUpdate, plotDiv: PlotlyHTMLElement): Promise<boolean> {
            this.isTimeSpan = sourceUpdate.update.isTimeSpan !== undefined && sourceUpdate.update.isTimeSpan === true;

            let plots = plotDiv.data as Plot[];

            let plotId = Layout.findCorrectId(plots, sourceUpdate.id);
            let lastTime = Layout.getLastXTime(plots, plotId);

            let prevData = plots[plotId];
            let prevId = prevData.ids !== undefined && prevData.ids?.length !== 0 ? prevData.ids.at(-1) : undefined;
            if (prevData.ids === undefined)
                prevData.ids = [];
            let redraw = false;

            for (let j of sourceUpdate.update.newVisibleValues) {
                if (lastTime >= new Date(j.time))
                    continue;

                if (this.isTimeSpan) {
                    let timespanValue = TimeSpanPlot.getTimeSpanValue(j);
                    this.singleUpdate.customdata.push(TimeSpanPlot.getTimeSpanCustomData(timespanValue, j))
                    this.singleUpdate.x.push(j.time)
                    this.singleUpdate.y.push(timespanValue === 'NaN' ? timespanValue : timespanValue.totalMilliseconds())
                } else {
                    if (prevId !== undefined && j.id === prevId) {
                        redraw = true;
                        prevData.x.pop();
                        prevData.y.pop();
                        prevData.customdata.pop();
                    }
                    this.singleUpdate.x.push(j.time);
                    this.singleUpdate.y.push(j.value);
                    prevData.ids.push(j.id)
                    let custom = j.value;

                    if (this.panel.range !== undefined && this.panel.range !== true)
                        custom = j.tooltip;
                    else if (j.tooltip !== null)
                        custom += `<br>${j.tooltip}`;

                    this.singleUpdate.customdata.push(custom);
                }
            }

            if (this.singleUpdate.x.length >= 1 && this.singleUpdate.y.length >= 1 && plots[plotId].x[0] === null) {
                window.Plotly.update(plotDiv, {x: [[]], y: [[]]}, {'xaxis.autorange': true}, plotId)
            }

            if (redraw) {
                prevData.x.push(...this.singleUpdate.x)
                prevData.y.push(...this.singleUpdate.y)
                prevData.customdata.push(...this.singleUpdate.customdata)

                this.redrawData.push({plot: prevData, id: plotId});
                
                // window.Plotly.deleteTraces(plotDiv, plotId);
                // window.Plotly.addTraces(plotDiv, prevData as Partial<Data>, plotId);
                // DefaultRelayout(plotDiv);
            } else {
                this.updateData.push({update: this.singleUpdate, id: plotId});
            }
            
            this.singleUpdate = new PlotUpdate();
            
            return new Promise<boolean>((resolve) => {
                resolve(true);
            });
        }

        private getUpdates() :[PlotUpdate, number[]] {
            let update: PlotUpdate = new PlotUpdate();
            let ids: number[] = [];

            this.updateData.map((x) => {
                update.x.push(x.update.x);
                update.y.push(x.update.y);
                update.customdata.push(x.update.customdata);
                ids.push(x.id);
            })
            
            return [update, ids];
        }
        
        extendTraces(plotDiv: PlotlyHTMLElement, update: PlotUpdate, ids: number[]) {
            window.Plotly.extendTraces(plotDiv, {
                y: update.y,
                x: update.x,
                customdata: update.customdata
            }, ids, Layout.maxPlottedPoints).then(
                (data) => {
                    if (this.isTimeSpan)
                        Layout.TimespanRelayout(data);
                    else
                        Layout.DefaultRelayout(data);

                    this.singleUpdate = new PlotUpdate();
                    this.updateData = [];
                    this.redrawData = [];
                }
            )
        }
    }
}
export namespace Layout {
    export const maxPlottedPoints = 1500;


    export function TimespanRelayout(data: any) {
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

    export function DefaultRelayout(data: any) {
        let layoutUpdate = {
            'xaxis.range': (window as any).getRangeDate(),
            'yaxis.autorange': true,
        }

        window.Plotly.relayout(data.id, layoutUpdate)
    }

    export function findCorrectId(plots: Plot[], sourceId: string): number {
        let correctId = 0;

        for (let j of plots) {
            if (j.id === sourceId)
                break;

            correctId += 1;
        }

        return correctId;
    }

    export function getLastXTime(plots: Plot[], plotId: number): Date {
        let lastTime = new Date(0);

        if (plots[plotId] !== undefined && plots[plotId].x.length > 0)
            lastTime = new Date(plots[plotId].x.at(-1));

        return lastTime;
    }
}