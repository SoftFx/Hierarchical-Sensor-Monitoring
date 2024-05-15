import {Dictionary, IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import {HttpPanelService} from "./services/http-panel-service";
import {ChartHelper} from "./chart-helper";
import Plotly from "plotly.js";
import {insertSourcePlot} from "../js/dashboard";
import {customReset} from "../js/plotting";
import {SiteHelper} from "./services/site-helper";
import {Panel} from "./dashboard.panel";
import getRangeDate = ChartHelper.getRangeDate;
import replaceHtmlToMarkdown = SiteHelper.replaceHtmlToMarkdown;

export const httpPanelService : HttpPanelService = new HttpPanelService();

export class DashboardStorage {
    containerHeight = 0;
    
    private _lastUpdateIntervalId: number;
    private panels: Dictionary<Panel> = {};
    
    id: string;

    public constructor() {
        this._lastUpdateIntervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(panel: Panel, lastUpdate: Date) {
        panel.lastUpdateTime = new Date(lastUpdate);

        this.panels[panel.id] = panel;
        this.panels[panel.id].basePanelInit();

        window.clearInterval(this._lastUpdateIntervalId)
        this._lastUpdateIntervalId = this.checkForUpdate(this.panels);

        $('#dashboardPanels')[0].style.minHeight = this.containerHeight + "px";
    }

    public getPanel(id: string): Panel {
        return this.panels[id];
    }
    
    public async initPanel(id: string, settings: IPanelSettings, ySettings: IYRangeSettings, values: any[], lastUpdate: Date, dId: string){
        let panel = new Panel(id, settings, ySettings);

        let result = await ChartHelper.initMultyichartCordinates(panel.settings, id)

        this.containerHeight = Math.max(this.containerHeight, result);

        let plot = await ChartHelper.initMultiChart(`panelChart_${id}`, panel.settings, Number((panel.settings.height * 1400).toFixed(5)) - 46)

        this.addPanel(panel, lastUpdate)

        if (!panel.settings.isSingleMode){
            values.forEach(function (x) {
                insertSourcePlot(x, `panelChart_${id}`, id, dId, panel.settings.range)
            })

            $(`#panelChart_${id}`).on('plotly_relayout', function (e, updateData){
                let emptypanel = $(`#emptypanel_${id}`);
                let container = $(`#${id}`);
                emptypanel.css('transform', `translate(${container.width() / 2 - emptypanel.width() / 2}px, ${container.height() / 2}px)`)
            }).on('plotly_doubleclick', async function(){
                await customReset($(`#panelChart_${id}`)[0], getRangeDate(), panel.settings.range)
            })

            await Plotly.relayout(plot.id, {
                'xaxis.autorange': false,
                'height': Number((settings.height * 1400).toFixed(5)) - 46
            })
        }
        
        if (values.length === 0) {
            $(`#emptypanel_${id}`).show();
        }
        
        replaceHtmlToMarkdown('panel_description')
    }
    
    checkForUpdate(panels: Dictionary<Panel>) {
        return window.setInterval(function () {
            Object.values(panels).forEach(panel => {
                panel.updateNotify();
            });
        }, 5000);
    }
}

