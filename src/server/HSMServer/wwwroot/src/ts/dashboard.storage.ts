import {Dictionary, IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import moment from "moment";
import {Layout} from "./plotUpdate";
import {PanelSettings} from "./dashboard.classes";
import {HttpPanelService} from "./services/http-panel-service";
import {ChartHelper} from "./chart-helper";
import getRangeDate = ChartHelper.getRangeDate;
import Plotly from "plotly.js";
import {insertSourcePlot} from "../js/dashboard";
import {customReset} from "../js/plotting";
import {SiteHelper} from "./services/site-helper";
import replaceHtmlToMarkdown = SiteHelper.replaceHtmlToMarkdown;

export const httpPanelService : HttpPanelService = new HttpPanelService();

export class DashboardStorage {
    containerHeight = 0;
    
    private _intervalId: number;
    private panels: Dictionary<Panel> = {};
    
    id: string;

    public constructor() {
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public addPanel(panel: Panel, lastUpdate: Date) {
        panel.lastUpdateTime = new Date(lastUpdate);

        this.panels[panel.id] = panel;
        this.panels[panel.id].basePanelInit();

        window.clearInterval(this._intervalId)
        this._intervalId = this.checkForUpdate(this.panels);
    }

    public getPanel(id: string): Panel {
        return this.panels[id];
    }
    
    public async initPanel(id: string, settings: IPanelSettings, ySettings: IYRangeSettings, values: any[], lastUpdate: Date, dId: string){
        let panel = new Panel(id, settings);

        let result = await ChartHelper.initMultyichartCordinates(settings,id)

        this.containerHeight = Math.max(this.containerHeight, result);

        let plot = await ChartHelper.initMultiChart(`panelChart_${id}`, Number((settings.height * 1400).toFixed(5)) - 46, settings.showLegend, false, ySettings.autoScale === true ? true : [Number(ySettings.minValue), Number(ySettings.maxValue)])

        this.addPanel(panel, lastUpdate)

        values.forEach(function (x) {
            insertSourcePlot(x, `panelChart_${id}`, id, dId, ySettings.autoScale === true ? true : [Number(ySettings.minValue), Number(ySettings.maxValue)])
        })

        $(`#panelChart_${id}`).on('plotly_relayout', function (e, updateData){
            let emptypanel = $(`#emptypanel_${id}`);
            let container = $(`#${id}`);
            emptypanel.css('transform', `translate(${container.width() / 2 - emptypanel.width() / 2}px, ${container.height() / 2}px)`)
        }).on('plotly_doubleclick', async function(){
            await customReset($(`#panelChart_${id}`)[0], getRangeDate(), ySettings.autoScale === true ? true : [Number(ySettings.minValue), Number(ySettings.maxValue)])
        })

        await Plotly.relayout(plot.id, {
            'xaxis.autorange': false,
            'height': Number((settings.height * 1400).toFixed(5)) - 46
        })

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

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;

    private _savebutton: JQuery<HTMLElement>;
    
    
    id: string
    settings: PanelSettings
    
    
    constructor(id: string, settings: IPanelSettings) {
        this.id = id;
        
        this.settings = new PanelSettings(this.id, settings);
    }
    
    get lastUpdateTime(): Date {
        return this._lastUpdateTime;
    }

    set lastUpdateTime(time: Date) {
        if (time > this._lastUpdateTime) {
            this._lastUpdateTime = time;
            this.updateNotify();
        }
    }

    updateNotify() {
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);

        if (this._lastUpdateTime.getTime() === 0)
            this._lastUpdateDiv.html("Never updated");
        else
            this._lastUpdateDiv.html(moment(this._lastUpdateTime).fromNow());
    }
    
    basePanelInit(){
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);

        if (!this.settings.isSingleMode){
            $('#selecthovermode_' + this.id).val(this.settings.hovermode);

            Layout.relayout(this.id, this.settings);

            this._savebutton = $('#selecthovermode_' + this.id);
            this._savebutton.on('change', async function (){
                this.settings.hovermode = Number($('#selecthovermode_' + this.id).val());

                await httpPanelService.updateSettings(this.settings);
                Layout.relayout(this.id, this.settings);
                $('#actionButton').trigger('click')
            }.bind(this))
        }

        this.updateNotify();
    }
}