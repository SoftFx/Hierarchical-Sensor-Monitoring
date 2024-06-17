export class MutationObserverService {
    initialValues: { [key: string]: string } = {};
    newValue: { [key: string]: string } = {};
    form: HTMLFormElement | null = null;
    
    alertChange: boolean = false;

    constructor() {
    }

    public addFormToObserve(id: string) {
        this.alertChange = false;
        this.form = document.getElementById(id) as HTMLFormElement;
        this.initialValues = this.getFormValues();
        this.newValue = structuredClone(this.initialValues);
        this.form.addEventListener('change', (event) => {
            if (event.target) {
                this.checkChanges(event.target as HTMLElement);
            }
        });
    }

    getFormValues(): { [key: string]: string } {
        const values: { [key: string]: string } = {};
        const elements = this.form.querySelectorAll('select, input, textarea');

        elements.forEach((element: HTMLElement) => {
            let closest = element.closest('.dataAlertRow');

            if (element.id || (element.getAttribute('name') && closest === null)) {
                if (element instanceof HTMLSelectElement) {
                    let currentValue = '';
                    if (element instanceof HTMLSelectElement) {
                        let arr = Array.from(element.selectedOptions);
                        for (let i of arr) {
                            currentValue += "," + i.value
                        }
                    }
                    values[element.id || element.name] = currentValue;
                } else if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
                    values[element.id || element.name] = element.value;
                }
            }
        })

        return values;
    }

    checkChanges(element: HTMLElement): void {
        let currentValue = '';
        let closest = element.closest('.dataAlertRow');

        if (closest !== null) {
            this.alertChange = true;
            return;
        }

        let id = element.id || element.getAttribute('name');

        if (element instanceof HTMLSelectElement) {
            let arr = Array.from(element.selectedOptions);
            for (let i of arr) {
                currentValue += "," + i.value
            }
        } else if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
            currentValue = element.value;
        }
        
        this.newValue[id] = currentValue;
    }

    public check(): boolean {
        if (this.alertChange)
            return false;
        
        for (let i of Object.entries(this.newValue)) {
            if (this.newValue[i[0]] !== this.initialValues[i[0]]) {
                return false;
            }
        }

        return true;
    }
}