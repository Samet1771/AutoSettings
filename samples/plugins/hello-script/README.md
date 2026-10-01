# Hello (sample script plugin)

A small script plugin that uses every part of the script plugin API. It has:

- a revertible action, `example.hello.write_text`;
- a condition, `example.hello.file_contains`;
- two triggers, `example.hello.file_added` and `example.hello.file_removed`. One poll script raises both.

## Try it

1. Copy the `1.0.0` folder to `%LocalAppData%\AutoSettings\plugins\example.hello\1.0.0`. AutoSettings picks it up
   within a few seconds, and the Activity page says "Plugins loaded in this app: example.hello 1.0.0".
2. Add this to your automations (Edit as YAML):

   ```yaml
   automations:
     - name: Hello plugin
       triggers:
         - type: example.hello.file_added
           name: "*.txt"
       actions:
         - type: notify
           message: "New file: {{ event.data.name }}"
         - type: example.hello.write_text
           path: '%USERPROFILE%\Desktop\hello.txt'
           text: "{{ user }} added {{ event.data.name }} at {{ time }}"
   ```

3. Create a `.txt` file in the folder `AutoSettings Hello` in your user folder (`%USERPROFILE%\AutoSettings Hello`).
   Within 5 seconds you get a notification and `hello.txt` appears on the desktop.

See the [script plugin guide](../../../docs/plugins/script-plugins.md) for how each script works.
