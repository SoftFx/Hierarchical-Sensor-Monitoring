import moment from "moment/moment";
import {DataUpdate, Layout} from "./plotUpdate";
import {SiteHelper} from "./services/site-helper";
import showToast = SiteHelper.showToast;
import Plotly from "plotly.js";
import {PanelSettings} from "./dashboard/dashboard.classes";
import {IPanelSettings, ISourceUpdate, IYRangeSettings} from "./dashboard/dashboard.interfaces";
import {httpPanelService, updateDashboardInterval} from "./dashboard/dashboard.storage";
import DataTable, {Order} from "datatables.net-dt";
import {Helper} from "./services/local-storage.helper";
import {OrderArray} from "datatables.net";

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;
    private _requestTimeout: number;

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

    initUpdateRequests() {
        let update = new DataUpdate.Update(this);
        this._requestTimeout = window.setInterval(function () {
            fetch(window.location.pathname + '/PanelUpdate' + `/${this.id}`, {
                method: 'GET'
            }).then(res => res.json())
                .then((res: ISourceUpdate[]) => {
                    update.updateSources(res)
                })
        }.bind(this), updateDashboardInterval)
    }

    basePanelInit() {
        if (this.id === 'multichart')
            return;

        this._lastUpdateDiv = $('#lastUpdate_' + this.id);

        this.addEventListeners();

        Layout.relayout(this.id, this.settings);

        this.updateNotify();
    }

    addOrderableTable() {
        let currentOrder = Helper.read<Order>(`singlemode_${this.id}`);
        
        $(`#${this.id} .orderable-table`).DataTable({
            search: false,
            paging: false,
            lengthChange: false,
            info: false,
            searching: false,
            order: currentOrder === null ? [0, 'asc'] : currentOrder
        }).on('order.dt', function(event: any, object :any, settings: any, plainSettings: any){
            Helper.save(`singlemode_${this.id}`, Object.entries(plainSettings));
        }.bind(this));
    }

    addEventListeners() {
        if (this.id === 'multichart')
            return;
        
        if (this.settings.isSingleMode)
            this.addOrderableTable();

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
                    } else {
                        Plotly.relayout(`panelChart_${this.id}`, {
                            // @ts-ignore
                            'legend.yref': "paper",
                            'showlegend': true,
                        }).then(
                            (success) => {
                                Plotly.relayout(`panelChart_${this.id}`, {
                                    'showlegend': this.settings.showLegend
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

    async manualCordinatesUpdate() {
        let currPanelDiv = $('#dashboardPanels').find(`#${this.id}`).first();
        let width = $('#dashboardPanels').width();
        let height = currPanelDiv.height();
        let maxHeight = 1400;
        return fetch(window.location.pathname + `/${this.id}`, {
            method: 'PUT',
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({
                isSingleMode: this.settings.isSingleMode,
                width: Number((currPanelDiv.width() / width).toFixed(5)),
                height: Number((height / maxHeight).toFixed(5)),
                x: Number((parseFloat(currPanelDiv.data('x') || 0) / width).toFixed(5)),
                y: Number((parseFloat(currPanelDiv.data('y') || 0) / maxHeight).toFixed(5)),
            }),
        })
    }
}