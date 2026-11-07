package com.godspace.outpostloop.update;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;

/** 仅向持有临时 URI 授权的系统安装器只读暴露专用目录内的 APK。 */
public final class ApkProvider extends ContentProvider {
    @Override public boolean onCreate() { return true; }

    private File resolve(Uri uri) throws FileNotFoundException {
        try {
            String name = uri.getLastPathSegment();
            if (!"content".equals(uri.getScheme()) ||
                !(getContext().getPackageName() + ".apkupdates").equals(uri.getAuthority()) ||
                uri.getPathSegments().size() != 1 || name == null || !name.matches("[0-9a-f]{64}\\.apk"))
                throw new FileNotFoundException("Invalid update URI");
            File root = new File(getContext().getFilesDir(), "ApkUpdates").getCanonicalFile();
            File file = new File(root, name).getCanonicalFile();
            if (!root.equals(file.getParentFile()) || !file.isFile()) throw new FileNotFoundException("Missing APK");
            return file;
        } catch (java.io.IOException failure) { throw new FileNotFoundException(failure.getMessage()); }
    }

    @Override public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode)) throw new FileNotFoundException("Read only");
        return ParcelFileDescriptor.open(resolve(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public String getType(Uri uri) { return "application/vnd.android.package-archive"; }
    @Override public Cursor query(Uri uri, String[] projection, String selection, String[] args, String sort) {
        try {
            File file = resolve(uri);
            String[] columns = projection == null ? new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE } : projection;
            MatrixCursor cursor = new MatrixCursor(columns, 1);
            Object[] row = new Object[columns.length];
            for (int i = 0; i < columns.length; ++i) {
                if (OpenableColumns.DISPLAY_NAME.equals(columns[i])) row[i] = "3DBase-update.apk";
                if (OpenableColumns.SIZE.equals(columns[i])) row[i] = file.length();
            }
            cursor.addRow(row);
            return cursor;
        } catch (FileNotFoundException failure) { return null; }
    }
    @Override public Uri insert(Uri uri, ContentValues values) { throw new UnsupportedOperationException("Read only"); }
    @Override public int update(Uri uri, ContentValues values, String selection, String[] args) { throw new UnsupportedOperationException("Read only"); }
    @Override public int delete(Uri uri, String selection, String[] args) { throw new UnsupportedOperationException("Read only"); }
}
