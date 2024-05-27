
export namespace Helper {
    export function read<T>(id: string) : T{
        let item = window.localStorage.getItem(id);

        return JSON.parse(item) as T;
    }

    export function save(id: string, item: any){
        window.localStorage.setItem(id, JSON.stringify(item));
    }
}
