mono .paket/paket.exe install -v
exit_code=$?
if [ $exit_code -ne 0 ]; then
	exit $exit_code
fi

mono packages/build/FAKE/tools/FAKE.exe $@ --fsiargs -d:MONO build.fsx 
