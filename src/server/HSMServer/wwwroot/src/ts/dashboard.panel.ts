import {PanelSettings} from "./dashboard.classes";
import {IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import moment from "moment/moment";
import {Layout} from "./plotUpdate";
import {httpPanelService} from "./dashboard.storage";
import {SiteHelper} from "./services/site-helper";
import showToast = SiteHelper.showToast;
import Plotly from "plotly.js";

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;

    id: string
    settings: PanelSettings
    updateSource: Function;

    constructor(id: string, settings: IPanelSettings, ySettings: IYRangeSettings) {
        this.id = id;
        this.settings = new PanelSettings(this.id, settings, ySettings);

        this.updateSource = this.getUpdateSourcesFunc();
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

    basePanelInit() {
        this._lastUpdateDiv = $('#lastUpdate_' + this.id);

        this.addEventListeners();
        
        Layout.relayout(this.id, this.settings);

        this.updateNotify();
    }

    getUpdateSourcesFunc() {
        return this.settings.isSingleMode ?
            this.updatePlot :
            this.updateSingleMode
    }

    updatePlot() {

    }

    updateSingleMode() {

    }

    addEventListeners() {
        let panel = document.getElementById(this.id);
        let actionButton = panel.querySelector('.action-button') as HTMLButtonElement
        let panelMenu = panel.querySelector('.dropdown-menu');

        let hovermode = panelMenu.querySelector('select.hovermode') as HTMLSelectElement;
        hovermode.value = this.settings.hovermode.toString();
        hovermode.addEventListener(
            "change",
            async (event) => {
                let target = event.target as HTMLSelectElement;
                this.settings.hovermode = Number(target.value);

                await httpPanelService.updateSettings(this);
                Layout.relayout(this.id, this.settings);
                actionButton.click();
            } 
        );
        
        panelMenu.querySelector('.switch-mode').addEventListener(
            "click",
            async (event) => {
                this.settings.isSingleMode = !this.settings.isSingleMode;
                
                let result = await httpPanelService.updateSettings(this);
                showToast(await result.text())
                actionButton.click();
            }
        );

        panelMenu.querySelector('.toggle-legend').addEventListener(
            "click",
            async (event: PointerEvent) => {
                let target = event.target as HTMLAnchorElement;
                this.settings.showLegend = !this.settings.showLegend;

                let result = await httpPanelService.updateSettings(this);
                showToast(await result.text());
                actionButton.click();

                if (!this.settings.isSingleMode && result.ok) {
                    if (this.settings.showLegend) {
                        Plotly.relayout(`panelChart_${this.id}`, {
                            // @ts-ignore
                            'legend.yref': "container",
                            'showlegend': this.settings.showLegend,
                        }).then(
                            (success) => {
                                target.textContent = "Hide legends";
                            },
                            (error) => {
                                showToast(error)
                            }
                        );
                    }
                    else {
                        Plotly.relayout(`panelChart_${this.id}`, {
                            // @ts-ignore
                            'legend.yref': "paper",
                            'showlegend': true,
                        }).then(
                            (success) => {
                                Plotly.relayout(`panelChart_${this.id}`, {
                                        'showlegend' : this.settings.showLegend
                                    })
                                target.textContent = "Show legends";
                            },
                            (error) => {
                                showToast(error)
                            }
                        );
                    }
                }
            }
        );
    }
}