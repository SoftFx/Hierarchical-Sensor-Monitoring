import moment from "moment/moment";
import {Layout} from "./plotUpdate";
import {SiteHelper} from "./services/site-helper";
import showToast = SiteHelper.showToast;
import Plotly from "plotly.js";
import {panelHelper} from "../js/dashboard";
import {PanelSettings} from "./dashboard/dashboard.classes";
import {IPanelSettings, IYRangeSettings} from "./dashboard/dashboard.interfaces";
import {httpPanelService} from "./dashboard/dashboard.storage";

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;

    id: string
    settings: PanelSettings

    constructor(id: string, settings: IPanelSettings, ySettings: IYRangeSettings) {
        this.id = id;
        this.settings = new PanelSettings(this.id, settings, ySettings);
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
                
                const panelPage = await httpPanelService.getPanel(this);
                actionButton.click();
                
                panel.replaceWith(createElementFromHTML(panelPage));
                
                function createElementFromHTML(htmlString: string) {
                    const range = document.createRange();
                    return range.createContextualFragment(htmlString);
                }
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