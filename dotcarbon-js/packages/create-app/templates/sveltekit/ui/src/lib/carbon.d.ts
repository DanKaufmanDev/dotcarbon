import '@dotcarbon/api';

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'app:greet': { args: { name: string }; result: string };
    }
}
