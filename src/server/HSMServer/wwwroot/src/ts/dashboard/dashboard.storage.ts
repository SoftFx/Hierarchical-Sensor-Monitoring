import {Dictionary, IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import {ChartHelper} from "../chart-helper";
import {SiteHelper} from "../services/site-helper";

import getRangeDate = ChartHelper.getRangeDate;
import replaceHtmlToMarkdown = SiteHelper.replaceHtmlToMarkdown;
import {Panel} from "../dashboard.panel";
import {HttpPanelService} from "../services/http-panel-service";
import {createChart, insertSourcePlot} from "../../js/dashboard";
import {customReset} from "../../js/plotting";
import Plotly from "plotly.js";
import {TimeSpanPlot} from "../../js/plots";
import {VersionPlot} from "../plots/version-plot";

export const httpPanelService : HttpPanelService = new HttpPanelService();
export const updateDashboardInterval = 120000;

export class DashboardStorage {
    containerHeight = 0;
    
    private _lastUpdateIntervalId: number;
    private panels: Dictionary<Panel> = {};
    
    id: string;

    public constructor() {
        this._lastUpdateIntervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(panel: Panel, lastUpdate: number) {
        panel.lastUpdateTime = new Date(lastUpdate);

        this.panels[panel.id] = panel;

        window.clearInterval(this._lastUpdateIntervalId)
        this._lastUpdateIntervalId = this.checkForUpdate(this.panels);
        
        $('#dashboardPanels')[0].style.minHeight = this.containerHeight + "px";
    }
    
    public basePanelInit() {
        for (const panel of Object.entries(this.panels))
            panel[1].basePanelInit()
    }

    public getPanel(id: string): Panel {
        return this.panels[id];
    }
  
    
    public async initPanel(id: string, settings: IPanelSettings, ySettings: IYRangeSettings, values: any[], lastUpdate: number, dId: string, sourceType: number, unit: string, range: boolean | [number, number] = undefined){
        let panel = new Panel(id, settings, ySettings, sourceType, unit);

        let result = await ChartHelper.initContrainerCordinates(panel.settings, id)

        this.containerHeight = Math.max(this.containerHeight, result);

        this.addPanel(panel, lastUpdate)

        if (!panel.settings.isSingleMode){
            const data : any[] = [];
            values.forEach(function (x) {
                data.push(insertSourcePlot(x, `panelChart_${id}`, id, dId, panel.settings.range)[0]);
            })

            let layout = {
                hovermode: 'closest',
                hoverdistance: 1,
                dragmode: 'zoom',
                autosize: true,
                height: Number((panel.settings.height * 1400).toFixed(5)) - 46,
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
                    visible: true,
                    autorange: false,
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
                    rangeslider: {
                        visible: false
                    }
                },
                yaxis: {
                    categoryorder : 'trace',
                    visible: true,
                    title: {
                        text: panel.unit,
                        font: {
                            size: 14,
                            color: '#7f7f7f'
                        }
                    },
                    tickmode: "auto",
                    // @ts-ignore
                    ticktext: [],
                    // @ts-ignore
                    tickvals: [],
                    tickfont: {
                        size: 10
                    },
                    // @ts-ignore
                    automargin: 'width+right'
                },
            }

            if (panel.sourceType === 7)
            {
                const ticks = TimeSpanPlot.getYaxisTicks(data);
                layout.yaxis.tickmode = 'array';
                layout.yaxis.ticktext = ticks.ticktext;
                layout.yaxis.tickvals = ticks.tickvals;
            }

            if (panel.sourceType === 8){

                const ticks = VersionPlot.getYaxisTicks(data);
                layout.yaxis.tickmode = 'array';
                layout.yaxis.ticktext = ticks.ticktext;
                layout.yaxis.tickvals = ticks.tickvals;
                layout.yaxis.categoryorder = ticks.categoryorder;
            }

            const config = {
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
                        click: (plot: any) => $(plot).trigger('plotly_doubleclick')
                    }],
                doubleClick: false
            }

            await createChart(`panelChart_${id}`, data, layout, config)

            $(`#panelChart_${id}`).on('plotly_relayout', function (e, updateData){
                let emptypanel = $(`#emptypanel_${id}`);
                let container = $(`#${id}`);
                emptypanel.css('transform', `translate(${container.width() / 2 - emptypanel.width() / 2}px, ${container.height() / 2}px)`)
            }).on('plotly_doubleclick', async function(){
                await customReset($(`#panelChart_${id}`)[0], getRangeDate(), panel.settings.range)
            })
            
            if (values.length === 0) {
                $(`#emptypanel_${id}`).show();
            }
        }

        panel.basePanelInit();
        await panel.manualCordinatesUpdate();

        replaceHtmlToMarkdown('panel_description')
    }
    
    checkForUpdate(panels: Dictionary<Panel>) {
        return window.setInterval(function () {
            Object.values(panels).forEach(panel => {
                panel.updateNotify();
            });
        }, 5000);
    }
    
    initUpdateRequests() {
        for (let i in this.panels)
            this.panels[i].initUpdateRequests()
    }
}

