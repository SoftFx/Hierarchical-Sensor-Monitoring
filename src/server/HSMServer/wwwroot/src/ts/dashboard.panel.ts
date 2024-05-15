import {PanelSettings} from "./dashboard.classes";
import {IPanelSettings, IYRangeSettings} from "./dashboard.interfaces";
import moment from "moment/moment";
import {Layout} from "./plotUpdate";
import {httpPanelService} from "./dashboard.storage";
import {SiteHelper} from "./services/site-helper";
import showToast = SiteHelper.showToast;

export class Panel {
    private _lastUpdateTime: Date = new Date(0);
    private _lastUpdateDiv: JQuery<HTMLElement>;

    private _savebutton: JQuery<HTMLElement>;


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

        $('#selecthovermode_' + this.id).val(this.settings.hovermode);

        Layout.relayout(this.id, this.settings);

        this._savebutton = $('#selecthovermode_' + this.id);
        this._savebutton.on('change', async function () {
            this.settings.hovermode = Number($('#selecthovermode_' + this.id).val());

            await httpPanelService.updateSettings(this.settings);
            Layout.relayout(this.id, this.settings);
            $('#actionButton').trigger('click')
        }.bind(this))
        

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
        document.getElementById(this.id).querySelector('.dropdown-menu .switch-mode').addEventListener(
            "click",
            () => {
                fetch(window.location.pathname + '/Panels', {
                    method: "put",
                    headers: {
                        "Content-Type": "application/json",
                    },
                    body: JSON.stringify({
                        id: this.id,
                        isSingleMode: !this.settings.isSingleMode
                    })
                }).then(
                    (data) => showToast("Panel mode updated!"),
                    (error) => showToast("Update failed")
                )
            }
        );
    }
}