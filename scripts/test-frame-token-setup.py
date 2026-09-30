import json, re, subprocess, sys, tempfile, unittest
from pathlib import Path

source = (Path(__file__).parent/'setup-token.ps1').read_text()
program = re.search(r"\$frameProgram = @'\n(.*?)\n'@", source, re.S).group(1)

class FrameTokenTests(unittest.TestCase):
    def test_discovery_save_replace_and_invalid_request(self):
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary)
            root = home/'.local/share/Steam/steamapps/compatdata/123'
            apk = root/'external/Android/data/com.frameearthvr.app/files/EarthVR'
            windows = root/'pfx/drive_c/users/steamuser/AppData/LocalLow/DefaultCompany/FrameEarthVR/EarthVR'
            apk.mkdir(parents=True); windows.mkdir(parents=True)
            test_program = program.replace('home = Path.home()', 'home = Path('+repr(str(home))+')')
            def run(request):
                return subprocess.run([sys.executable,'-c',test_program],input=json.dumps(request),text=True,capture_output=True)
            for platform, folder in [('apk',apk),('windows',windows)]:
                found = run(dict(mode='discover',platform=platform))
                self.assertEqual(found.returncode,0,found.stderr)
                self.assertEqual(json.loads(found.stdout),[str(folder)])
                for token in ['user-test-first','user-test-replacement']:
                    saved = run(dict(mode='save',platform=platform,folder=str(folder),token=token))
                    self.assertEqual(saved.returncode,0,saved.stderr)
                    self.assertNotIn(token,saved.stdout+saved.stderr)
                    self.assertEqual(json.loads((folder/'cesium-ion.local.json').read_text())['accessToken'],token)
                bad = run(dict(mode='save',platform=platform,folder=str(folder),token='two words'))
                self.assertNotEqual(bad.returncode,0)
                self.assertEqual(json.loads((folder/'cesium-ion.local.json').read_text())['accessToken'],'user-test-replacement')
                self.assertEqual(list(folder.glob('*.tmp')),[])
            bad = run(dict(mode='save',platform='apk',folder=str(home),token='valid-test'))
            self.assertNotEqual(bad.returncode,0)
            self.assertFalse((home/'cesium-ion.local.json').exists())

if __name__=='__main__': unittest.main()
